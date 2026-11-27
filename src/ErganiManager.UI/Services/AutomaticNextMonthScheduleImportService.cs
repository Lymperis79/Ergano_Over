using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.ErganiApi.Services;
using ErganiManager.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ErganiManager.UI.Services;

/// <summary>
/// Runs the opt-in next-month Ergani schedule download while an administrator is logged in.
/// The workbook is handed to SchedulesViewModel so the exact same authoritative importer is used.
/// </summary>
public sealed class AutomaticNextMonthScheduleImportService : IDisposable
{
    private readonly IPortalSettingsService _settings;
    private readonly IErganiPortalService _portal;
    private readonly ICompanyContext _companyContext;
    private readonly IOfficialHolidayCatalogService _holidays;
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;
    private UserSession? _session;
    private SchedulesViewModel? _importViewModel;
    private bool _awaitingDownload;
    private DateTime _retryAfterLocal = DateTime.MinValue;
    private string _runningMonthKey = string.Empty;

    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ErganiManager", "automatic-next-month-import.txt");

    public AutomaticNextMonthScheduleImportService(
        IPortalSettingsService settings,
        IErganiPortalService portal,
        ICompanyContext companyContext,
        IOfficialHolidayCatalogService holidays,
        IServiceProvider services)
    {
        _settings = settings;
        _portal = portal;
        _companyContext = companyContext;
        _holidays = holidays;
        _services = services;
        _portal.ExcelDownloaded += OnExcelDownloaded;
    }

    public void Start(UserSession session)
    {
        _session = session;
        _timer ??= new Timer(_ => _ = CheckAsync(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        _ = CheckAsync();
    }

    public void Stop()
    {
        _session = null;
        _timer?.Dispose();
        _timer = null;
        _importViewModel?.SetAutomaticPortalImportMode(false);
        _importViewModel = null;
        _awaitingDownload = false;
        _runningMonthKey = string.Empty;
    }

    private async Task CheckAsync()
    {
        if (_session == null || !_settings.Current.AutoDownloadNextMonthEnabled ||
            _awaitingDownload || DateTime.Now < _retryAfterLocal)
            return;

        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            var now = DateTime.Now;
            var targetMonth = new DateOnly(now.Year, now.Month, 1).AddMonths(1);
            var monthKey = targetMonth.ToString("yyyy-MM");
            if (string.Equals(ReadCompletedMonth(), monthKey, StringComparison.Ordinal))
                return;

            var companyId = _session.CompanyId ?? _companyContext.ActiveCompanyId;
            if (!companyId.HasValue)
                return;

            var lastWorkingDay = await GetLastWorkingDayAsync(now.Year, now.Month).ConfigureAwait(false);
            if (DateOnly.FromDateTime(now) != lastWorkingDay ||
                now.TimeOfDay < _settings.Current.AutoDownloadNextMonthTime)
                return;

            if (_runningMonthKey == monthKey)
                return;

            _runningMonthKey = monthKey;
            _awaitingDownload = true;

            var sessionForCompany = new UserSession
            {
                UserId = _session.UserId,
                Username = _session.Username,
                Role = _session.Role,
                CompanyId = companyId,
                CompanyName = _session.CompanyName,
                BranchId = _session.BranchId,
                BranchName = _session.BranchName,
                IsOfflineSession = _session.IsOfflineSession,
                CanSaveCredentials = _session.CanSaveCredentials,
                AllowedMenus = _session.AllowedMenus
            };

            _importViewModel = _services.GetRequiredService<SchedulesViewModel>();
            await Dispatcher.UIThread.InvokeAsync(async () =>
                await _importViewModel.InitializeForAutomaticImportAsync(sessionForCompany));

            _importViewModel.SetAutomaticPortalImportMode(true);

            var from = targetMonth;
            var to = targetMonth.AddMonths(1).AddDays(-1);
            Log.Information("Starting automatic Ergani schedule download for company {CompanyId}, period {From}–{To}.",
                companyId.Value, from, to);

            var result = await _portal.OpenAutomaticScheduleDownloadAsync(companyId.Value, from, to)
                .ConfigureAwait(false);
            Log.Information("Automatic Ergani schedule portal result: {Result}", result);

            if (result.StartsWith("❌", StringComparison.Ordinal))
            {
                FailAttempt(result);
                return;
            }

            _ = WatchForDownloadTimeoutAsync(monthKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Automatic next-month Ergani schedule import could not be started.");
            FailAttempt(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DateOnly> GetLastWorkingDayAsync(int year, int month)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var officialDates = new System.Collections.Generic.HashSet<DateOnly>();
        try
        {
            var records = await _holidays.GetForYearsAsync(new[] { year }).ConfigureAwait(false);
            foreach (var record in records)
                officialDates.Add(record.Date);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the official Greek holiday calendar; using the built-in holiday list.");
        }

        var fallbackDates = GreekHolidays.ForYear(year).Select(h => h.Date).ToHashSet();
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
               officialDates.Contains(date) || fallbackDates.Contains(date))
            date = date.AddDays(-1);
        return date;
    }

    private void OnExcelDownloaded(string filePath)
    {
        if (!_awaitingDownload)
            return;
        _ = ImportDownloadedFileAsync(filePath);
    }

    private async Task ImportDownloadedFileAsync(string filePath)
    {
        try
        {
            var vm = _importViewModel;
            if (vm == null)
                throw new InvalidOperationException("The automatic schedule importer was not initialized.");

            var imported = false;
            await Dispatcher.UIThread.InvokeAsync(async () =>
                imported = await vm.ImportDownloadedAutomaticallyAsync(filePath));

            if (!imported)
                throw new InvalidOperationException("The downloaded Excel file could not be imported. Check the Schedules status and application log.");

            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, _runningMonthKey);
            Log.Information("Automatic Ergani schedule import completed for {Month}.", _runningMonthKey);
            _awaitingDownload = false;
            _retryAfterLocal = DateTime.MaxValue;
            vm.SetAutomaticPortalImportMode(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Automatic Ergani schedule Excel import failed.");
            FailAttempt(ex.Message);
        }
    }

    private async Task WatchForDownloadTimeoutAsync(string monthKey)
    {
        await Task.Delay(TimeSpan.FromMinutes(3)).ConfigureAwait(false);
        if (!_awaitingDownload || _runningMonthKey != monthKey)
            return;
        FailAttempt("The Ergani portal did not produce an Excel download within three minutes.");
    }

    private void FailAttempt(string reason)
    {
        _awaitingDownload = false;
        _retryAfterLocal = DateTime.Now.AddMinutes(15);
        _importViewModel?.SetAutomaticPortalImportMode(false);
        Log.Warning("Automatic next-month schedule import will retry later: {Reason}", reason);
    }

    private static string ReadCompletedMonth()
    {
        try { return File.Exists(MarkerPath) ? File.ReadAllText(MarkerPath).Trim() : string.Empty; }
        catch { return string.Empty; }
    }

    public void Dispose()
    {
        Stop();
        _portal.ExcelDownloaded -= OnExcelDownloaded;
        _gate.Dispose();
    }
}
