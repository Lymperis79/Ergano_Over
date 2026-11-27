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
    private int _runningCompanyId;
    private string _cachedWorkingMonth = string.Empty;
    private DateOnly _cachedLastWorkingDay;
    private TaskCompletionSource<bool>? _testCompletion;
    private bool _isTestRun;

    /// <summary>Runs a one-off download/import for the next calendar month without changing the scheduled-run marker.</summary>
    public async Task RunTestAsync(UserSession session, Action<string> reportStatus)
    {
        if (session.Role != AppUserRole.Admin)
            throw new InvalidOperationException("Only an administrator can run the automatic schedule import test.");
        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            throw new InvalidOperationException("An automatic schedule download or import is already running.");

        try
        {
            if (_awaitingDownload)
                throw new InvalidOperationException("An automatic schedule download or import is already running.");

            var companyId = session.CompanyId ?? _companyContext.ActiveCompanyId;
            if (!companyId.HasValue)
                throw new InvalidOperationException("Select a company before testing the automatic schedule import.");

            var targetMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
            var monthKey = targetMonth.ToString("yyyy-MM");
            var testSession = new UserSession
            {
                UserId = session.UserId,
                Username = session.Username,
                Role = session.Role,
                CompanyId = companyId,
                CompanyName = session.CompanyName,
                BranchId = session.BranchId,
                BranchName = session.BranchName,
                IsOfflineSession = session.IsOfflineSession,
                CanSaveCredentials = session.CanSaveCredentials,
                AllowedMenus = session.AllowedMenus
            };

            _runningMonthKey = monthKey;
            _runningCompanyId = companyId.Value;
            _awaitingDownload = true;
            _isTestRun = true;
            var testCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _testCompletion = testCompletion;

            _importViewModel ??= SchedulesViewModel.Latest ?? _services.GetRequiredService<SchedulesViewModel>();
            reportStatus($"Preparing the schedule importer for {targetMonth:MMMM yyyy}…");
            await RunOnUiAsync(async () =>
            {
                await _importViewModel.InitializeForAutomaticImportAsync(testSession);
                return true;
            });
            _importViewModel.SetAutomaticPortalImportMode(true);

            reportStatus($"Opening the configured Ergani portal for {targetMonth:MMMM yyyy}…");
            var from = targetMonth;
            var to = targetMonth.AddMonths(1).AddDays(-1);
            var result = await _portal.OpenAutomaticScheduleDownloadAsync(companyId.Value, from, to)
                .ConfigureAwait(false);
            if (result.StartsWith("❌", StringComparison.Ordinal))
                throw new InvalidOperationException(result);

            reportStatus("Waiting for the Excel file to download…");
            var completed = await testCompletion.Task.WaitAsync(TimeSpan.FromMinutes(8)).ConfigureAwait(false);
            if (!completed)
                throw new InvalidOperationException("The schedule import test did not complete.");
            reportStatus($"✅ Test download and import completed for {targetMonth:MMMM yyyy}. The monthly automatic-run marker was not changed.");
        }
        catch (Exception ex)
        {
            _awaitingDownload = false;
            _isTestRun = false;
            _testCompletion = null;
            _importViewModel?.SetAutomaticPortalImportMode(false);
            _runningMonthKey = string.Empty;
            _runningCompanyId = 0;
            reportStatus("❌ Test failed: " + ex.Message);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string MarkerPath(int companyId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ErganiManager", $"automatic-next-month-import-{companyId}.txt");

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
        if (session.Role != AppUserRole.Admin)
            return;
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
        _runningCompanyId = 0;
    }

    private async Task CheckAsync()
    {
        var activeSession = _session;
        if (activeSession == null || !_settings.Current.AutoDownloadNextMonthEnabled ||
            _awaitingDownload || DateTime.Now < _retryAfterLocal)
            return;

        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            var now = DateTime.Now;
            var targetMonth = new DateOnly(now.Year, now.Month, 1).AddMonths(1);
            var monthKey = targetMonth.ToString("yyyy-MM");
            var companyId = activeSession.CompanyId ?? _companyContext.ActiveCompanyId;
            if (!companyId.HasValue)
                return;
            if (string.Equals(ReadCompletedMonth(companyId.Value), monthKey, StringComparison.Ordinal))
                return;

            DateOnly lastWorkingDay;
            if (_cachedWorkingMonth == $"{now.Year:D4}-{now.Month:D2}")
                lastWorkingDay = _cachedLastWorkingDay;
            else
            {
                lastWorkingDay = await GetLastWorkingDayAsync(now.Year, now.Month).ConfigureAwait(false);
                _cachedWorkingMonth = $"{now.Year:D4}-{now.Month:D2}";
                _cachedLastWorkingDay = lastWorkingDay;
            }
            if (DateOnly.FromDateTime(now) != lastWorkingDay ||
                now.TimeOfDay < _settings.Current.AutoDownloadNextMonthTime)
                return;

            if (_runningMonthKey == monthKey && _runningCompanyId == companyId.Value)
                return;

            _runningMonthKey = monthKey;
            _runningCompanyId = companyId.Value;
            _awaitingDownload = true;

            var sessionForCompany = new UserSession
            {
                UserId = activeSession.UserId,
                Username = activeSession.Username,
                Role = activeSession.Role,
                CompanyId = companyId,
                CompanyName = activeSession.CompanyName,
                BranchId = activeSession.BranchId,
                BranchName = activeSession.BranchName,
                IsOfflineSession = activeSession.IsOfflineSession,
                CanSaveCredentials = activeSession.CanSaveCredentials,
                AllowedMenus = activeSession.AllowedMenus
            };

            _importViewModel ??= SchedulesViewModel.Latest ?? _services.GetRequiredService<SchedulesViewModel>();
            await RunOnUiAsync(async () =>
            {
                await _importViewModel.InitializeForAutomaticImportAsync(sessionForCompany);
                return true;
            });

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
            imported = await RunOnUiAsync(() => vm.ImportDownloadedAutomaticallyAsync(filePath));

            if (!imported)
                throw new InvalidOperationException("The downloaded Excel file could not be imported. Check the Schedules status and application log.");

            var wasTestRun = _isTestRun;
            if (!wasTestRun)
            {
                var completedCompanyId = _runningCompanyId;
                if (completedCompanyId <= 0)
                    throw new InvalidOperationException("The automatic import company could not be determined.");
                Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath(completedCompanyId))!);
                File.WriteAllText(MarkerPath(completedCompanyId), _runningMonthKey);
                Log.Information("Automatic Ergani schedule import completed for {Month}.", _runningMonthKey);
            }
            else
            {
                Log.Information("Manual test of automatic Ergani schedule import completed for {Month}; scheduled-run marker was not changed.", _runningMonthKey);
            }
            _awaitingDownload = false;
            _retryAfterLocal = DateTime.MinValue;
            _isTestRun = false;
            vm.SetAutomaticPortalImportMode(false);
            await RunOnUiAsync(async () =>
            {
                await SchedulesViewModel.RefreshLatestCalendarAsync();
                return true;
            });
            _testCompletion?.TrySetResult(true);
            _testCompletion = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Automatic Ergani schedule Excel import failed.");
            if (_testCompletion != null)
            {
                _testCompletion.TrySetException(ex);
                _testCompletion = null;
                _isTestRun = false;
                _awaitingDownload = false;
                _runningMonthKey = string.Empty;
                _runningCompanyId = 0;
                _importViewModel?.SetAutomaticPortalImportMode(false);
            }
            else
            {
                FailAttempt(ex.Message);
            }
        }
    }

    private async Task WatchForDownloadTimeoutAsync(string monthKey)
    {
        await Task.Delay(TimeSpan.FromMinutes(8)).ConfigureAwait(false);
        if (!_awaitingDownload || _runningMonthKey != monthKey)
            return;
        FailAttempt("The Ergani portal did not produce an Excel download within eight minutes.");
    }

    private void FailAttempt(string reason)
    {
        _awaitingDownload = false;
        _retryAfterLocal = DateTime.Now.AddMinutes(15);
        _importViewModel?.SetAutomaticPortalImportMode(false);
        _runningMonthKey = string.Empty;
        Log.Warning("Automatic next-month schedule import will retry later: {Reason}", reason);
    }

    private static async Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { completion.TrySetResult(await action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private static string ReadCompletedMonth(int companyId)
    {
        try { return File.Exists(MarkerPath(companyId)) ? File.ReadAllText(MarkerPath(companyId)).Trim() : string.Empty; }
        catch { return string.Empty; }
    }

    public void Dispose()
    {
        Stop();
        _portal.ExcelDownloaded -= OnExcelDownloaded;
        _gate.Dispose();
    }
}
