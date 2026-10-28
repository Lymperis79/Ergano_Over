using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;
using ErganiManager.UI.Services;

namespace ErganiManager.UI.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IConnectionStateService _connectionState;
    private readonly AutomaticDatabaseBackupService _automaticBackup;
    private UserSession? _session;

    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private int _branchCount;
    [ObservableProperty] private int _employeeCount;
    [ObservableProperty] private int _submissionCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _successfulCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private string _periodText = string.Empty;
    [ObservableProperty] private string _lastUpdatedText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasCompany;
    [ObservableProperty] private string _backupStatus = string.Empty;
    [ObservableProperty] private string _backupLastSuccess = string.Empty;
    [ObservableProperty] private string _backupNextRun = string.Empty;

    public bool CanRefresh => !IsLoading;
    public bool HasCategories => Categories.Count > 0;
    public bool NoCategories => Categories.Count == 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;
    public bool NoRecentActivity => RecentActivity.Count == 0;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public ObservableCollection<DashboardCategoryRow> Categories { get; } = new();
    public ObservableCollection<DashboardActivityRow> RecentActivity { get; } = new();

    public DashboardViewModel(IConnectionStateService connectionState, AutomaticDatabaseBackupService automaticBackup)
    {
        _connectionState = connectionState;
        _automaticBackup = automaticBackup;
        PeriodText = Loc["Dashboard.CurrentMonth"];
        RefreshBackupSummary();
    }

    private void RefreshBackupSummary()
    {
        BackupStatus = _automaticBackup.Status;
        BackupLastSuccess = _automaticBackup.LastSuccessLocal is { } last
            ? last.ToString("dd/MM/yyyy HH:mm") : Loc["Dashboard.NotYet"];
        BackupNextRun = _automaticBackup.NextRunLocal is { } next
            ? next.ToString("dd/MM/yyyy HH:mm") : Loc["Dashboard.NotScheduled"];
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasCompany = session.CompanyId.HasValue;
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = string.Empty;
        RefreshBackupSummary();

        try
        {
            var companyId = _session?.CompanyId;
            if (!companyId.HasValue)
            {
                CompanyName = Loc["Dashboard.NoCompany"];
                BranchCount = EmployeeCount = SubmissionCount = ErrorCount = PendingCount = 0;
                SuccessfulCount = FailedCount = 0;
                Categories.Clear();
                RecentActivity.Clear();
                OnPropertyChanged(nameof(HasCategories));
                OnPropertyChanged(nameof(NoCategories));
                OnPropertyChanged(nameof(HasRecentActivity));
                OnPropertyChanged(nameof(NoRecentActivity));
                HasCompany = false;
                StatusMessage = Loc["Dashboard.SelectCompany"];
                return;
            }

            HasCompany = true;
            var options = _connectionState.GetDbOptions();
            await using var db = new AppDbContext(options);

            var company = await db.Companies.AsNoTracking()
                .Where(c => c.Id == companyId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();
            CompanyName = company ?? Loc["Dashboard.UnknownCompany"];

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonth = monthStart.AddMonths(1);
            var recentStart = DateTime.UtcNow.AddDays(-30);

            BranchCount = await db.Branches.CountAsync(b => b.CompanyId == companyId.Value && b.IsActive);
            EmployeeCount = await db.Employees.CountAsync(e => e.CompanyId == companyId.Value && e.IsActive);

            var companyLogs = db.ApiSubmissionLogs.AsNoTracking()
                .Where(l => l.CompanyId == companyId.Value);

            SubmissionCount = await companyLogs.CountAsync(l => l.SubmissionDate >= monthStart && l.SubmissionDate < nextMonth);
            SuccessfulCount = await companyLogs.CountAsync(l => l.SubmissionDate >= monthStart && l.SubmissionDate < nextMonth && l.Success);
            FailedCount = await companyLogs.CountAsync(l => l.SubmissionDate >= recentStart && !l.Success);
            ErrorCount = FailedCount;

            var pendingSchedules = db.Schedules.Where(s =>
                s.Employee != null && s.Employee.CompanyId == companyId.Value && !s.SubmittedToErgani);
            var pendingOvertimes = db.Overtimes.Where(o =>
                o.Employee != null && o.Employee.CompanyId == companyId.Value && !o.SubmittedToErgani && !o.IsCancelled);
            var pendingLeaves = db.Leaves.Where(l =>
                l.Employee != null && l.Employee.CompanyId == companyId.Value && !l.SubmittedToErgani);
            PendingCount = await pendingSchedules.CountAsync()
                         + await pendingOvertimes.CountAsync()
                         + await pendingLeaves.CountAsync();

            Categories.Clear();
            var categoryCounts = await companyLogs
                .Where(l => l.SubmissionDate >= monthStart && l.SubmissionDate < nextMonth)
                .GroupBy(l => l.SubmissionType)
                .Select(g => new { Type = g.Key, Total = g.Count(), Successful = g.Count(l => l.Success) })
                .OrderByDescending(g => g.Total)
                .ToListAsync();

            var max = Math.Max(1, categoryCounts.Select(x => x.Total).DefaultIfEmpty(0).Max());
            foreach (var item in categoryCounts)
            {
                Categories.Add(new DashboardCategoryRow
                {
                    Name = FormatSubmissionType(item.Type),
                    Total = item.Total,
                    Successful = item.Successful,
                    Failed = item.Total - item.Successful,
                    MaxCount = max
                });
            }

            OnPropertyChanged(nameof(HasCategories));
            OnPropertyChanged(nameof(NoCategories));

            RecentActivity.Clear();
            var latest = await companyLogs
                .OrderByDescending(l => l.SubmissionDate)
                .Take(8)
                .Select(l => new
                {
                    l.SubmissionDate,
                    l.SubmissionType,
                    l.Success,
                    l.Protocol,
                    l.ErrorMessage,
                    l.HttpStatusCode,
                    l.EmployeeId,
                    l.EmployeeIdsJson,
                    l.WorkCardId,
                    l.ScheduleDate
                })
                .ToListAsync();

            // Resolve employee names for both single-employee and multi-employee submission batches.
            var employeeIds = new HashSet<int>();
            var workCardIds = latest.Where(x => x.WorkCardId.HasValue)
                .Select(x => x.WorkCardId!.Value).Distinct().ToList();

            foreach (var item in latest)
            {
                if (item.EmployeeId.HasValue)
                    employeeIds.Add(item.EmployeeId.Value);

                if (!string.IsNullOrWhiteSpace(item.EmployeeIdsJson))
                {
                    try
                    {
                        var ids = JsonSerializer.Deserialize<List<int>>(item.EmployeeIdsJson);
                        if (ids != null)
                            foreach (var id in ids)
                                employeeIds.Add(id);
                    }
                    catch (JsonException)
                    {
                        // Older or malformed historical records remain visible without employee names.
                    }
                }
            }

            var workCards = workCardIds.Count == 0
                ? new Dictionary<int, WorkCard>()
                : await db.WorkCards.AsNoTracking()
                    .Where(w => workCardIds.Contains(w.Id))
                    .ToDictionaryAsync(w => w.Id);

            foreach (var card in workCards.Values)
                employeeIds.Add(card.EmployeeId);

            var employeeNames = employeeIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.Employees.AsNoTracking()
                    .Where(e => e.CompanyId == companyId.Value && employeeIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => e.FullName);

            foreach (var item in latest)
            {
                var names = new List<string>();
                if (item.EmployeeId.HasValue && employeeNames.TryGetValue(item.EmployeeId.Value, out var singleName))
                    names.Add(singleName);

                if (!string.IsNullOrWhiteSpace(item.EmployeeIdsJson))
                {
                    try
                    {
                        var ids = JsonSerializer.Deserialize<List<int>>(item.EmployeeIdsJson);
                        if (ids != null)
                        {
                            foreach (var id in ids)
                            {
                                if (employeeNames.TryGetValue(id, out var name) && !names.Contains(name))
                                    names.Add(name);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Keep showing the rest of the submission information for legacy rows.
                    }
                }

                WorkCard? workCard = null;
                if (item.WorkCardId.HasValue)
                    workCards.TryGetValue(item.WorkCardId.Value, out workCard);

                if (workCard != null && employeeNames.TryGetValue(workCard.EmployeeId, out var cardEmployeeName)
                    && !names.Contains(cardEmployeeName))
                    names.Add(cardEmployeeName);

                var subjectParts = new List<string>();
                if (names.Count > 0)
                    subjectParts.Add(string.Format(Loc["Dashboard.SubjectEmployees"], string.Join(", ", names)));
                else
                    subjectParts.Add(Loc["Dashboard.SubjectEmployeeUnknown"]);

                if (item.ScheduleDate.HasValue)
                    subjectParts.Add(string.Format(Loc["Dashboard.SubjectDate"], item.ScheduleDate.Value.ToString("dd/MM/yyyy")));

                if (workCard != null)
                {
                    var movement = workCard.MovementType == MovementType.Arrival
                        ? Loc["Dashboard.WorkCard.Arrival"]
                        : Loc["Dashboard.WorkCard.Departure"];
                    subjectParts.Add(string.Format(Loc["Dashboard.WorkCardEvent"], movement,
                        workCard.MovementDateTime.ToLocalTime().ToString("dd/MM/yyyy HH:mm")));
                }

                RecentActivity.Add(new DashboardActivityRow
                {
                    DateText = item.SubmissionDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    TypeText = FormatSubmissionType(item.SubmissionType),
                    SubjectText = string.Join(" · ", subjectParts),
                    StatusText = item.Success ? "✓ " + Loc["Dashboard.Success"] : "✕ " + Loc["Dashboard.Failed"],
                    DetailText = item.Success
                        ? (!string.IsNullOrWhiteSpace(item.Protocol)
                            ? string.Format(Loc["Dashboard.Protocol"], item.Protocol)
                            : Loc["Dashboard.SubmissionCompleted"])
                        : (item.ErrorMessage ?? (item.HttpStatusCode.HasValue
                            ? string.Format(Loc["Dashboard.HttpError"], item.HttpStatusCode.Value)
                            : Loc["Dashboard.NoErrorDetails"])),
                    IsSuccess = item.Success
                });
            }

            OnPropertyChanged(nameof(HasRecentActivity));
            OnPropertyChanged(nameof(NoRecentActivity));
            LastUpdatedText = string.Format(Loc["Dashboard.LastUpdated"], DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            if (Categories.Count == 0)
                StatusMessage = Loc["Dashboard.NoSubmissionsInPeriod"];
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc["Dashboard.LoadFailed"], ex.Message);
            Serilog.Log.Error(ex, "Failed to load company dashboard for company {CompanyId}", _session?.CompanyId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string FormatSubmissionType(string? type)
    {
        var key = (type ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "dailyschedule" or "schedule" or "schedules" or "wtos" => Loc["Dashboard.Type.Schedules"],
            "overtime" or "wto" or "wtoov" => Loc["Dashboard.Type.Overtime"],
            "workcard" or "workcards" or "wrkcardse" or "workcardse" => Loc["Dashboard.Type.WorkCards"],
            "leave" or "holiday" or "holidays" or "wtoleave" => Loc["Dashboard.Type.Leaves"],
            "" => Loc["Dashboard.Type.Other"],
            _ => type!
        };
    }
}

public sealed class DashboardCategoryRow
{
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Successful { get; set; }
    public string SuccessfulText => $"✓ {Successful}";
    public int Failed { get; set; }
    public bool HasFailures => Failed > 0;
    public string FailedText => $"✕ {Failed}";
    public int MaxCount { get; set; }
}

public sealed class DashboardActivityRow
{
    public string DateText { get; set; } = string.Empty;
    public string TypeText { get; set; } = string.Empty;
    public string SubjectText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
}
