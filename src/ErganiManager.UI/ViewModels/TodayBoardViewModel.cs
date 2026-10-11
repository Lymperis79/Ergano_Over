using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

/// <summary>
/// Daily status board: every active employee with the plan, clock-in / clock-out and the Ergani
/// status of each movement, and a list of problems (late, missing, not submitted...).
/// </summary>
public partial class TodayBoardViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly ITodayStatusService _statusService;
    private UserSession? _session;
    private List<TodayEmployeeStatusDto> _all = new();
    private DispatcherTimer? _timer;
    private bool _refreshing;

    public ObservableCollection<TodayEmployeeStatusDto> Rows { get; } = new();
    public int[] GraceOptions { get; } = { 0, 5, 10, 15, 30 };

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private DateTimeOffset? _selectedDate = new DateTimeOffset(DateTime.Today);
    [ObservableProperty] private int _graceMinutes = 5;
    [ObservableProperty] private bool _onlyProblems;
    [ObservableProperty] private bool _autoRefresh = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _lastUpdatedText = string.Empty;
    [ObservableProperty] private TodayEmployeeStatusDto? _selectedRow;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _workingCount;
    [ObservableProperty] private int _leftCount;
    [ObservableProperty] private int _notArrivedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;

    public TodayBoardViewModel(ITodayStatusService statusService)
    {
        _statusService = statusService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        _ = RefreshAsync();
    }

    partial void OnSelectedDateChanged(DateTimeOffset? value) => _ = RefreshAsync();
    partial void OnGraceMinutesChanged(int value) => _ = RefreshAsync();
    partial void OnOnlyProblemsChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        if (_session?.CompanyId is not int companyId)
        {
            HasActiveCompany = false;
            return;
        }

        _refreshing = true;
        IsLoading = true;
        try
        {
            var date = DateOnly.FromDateTime((SelectedDate ?? DateTimeOffset.Now).DateTime);
            _all = await _statusService.GetAsync(companyId, date, GraceMinutes);
            ApplyFilter();
            LastUpdatedText = $"Updated {DateTime.Now:HH:mm:ss}";
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _refreshing = false;
        }
    }

    [RelayCommand]
    private void GoToday() => SelectedDate = new DateTimeOffset(DateTime.Today);

    private void ApplyFilter()
    {
        var keepId = SelectedRow?.EmployeeId;

        TotalCount      = _all.Count(r => r.IsExpectedToday || r.IsWorking || r.HasLeft);
        WorkingCount    = _all.Count(r => r.IsWorking);
        LeftCount       = _all.Count(r => r.HasLeft);
        NotArrivedCount = _all.Count(r => r.IsNotArrived);
        ErrorCount      = _all.Count(r => r.IsError);
        WarningCount    = _all.Count(r => r.IsWarning);

        Rows.Clear();
        foreach (var r in _all.Where(r => !OnlyProblems || r.HasIssues))
            Rows.Add(r);

        SelectedRow = keepId.HasValue ? Rows.FirstOrDefault(r => r.EmployeeId == keepId.Value) : null;
    }

    // ── auto refresh: runs only while the board is on screen (the view starts/stops it) ──

    public void StartAutoRefresh()
    {
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        if (_session != null) _ = RefreshAsync();
    }

    public void StopAutoRefresh() => _timer?.Stop();

    private async void OnTick(object? sender, EventArgs e)
    {
        // Only today's board changes by itself.
        var isToday = (SelectedDate ?? DateTimeOffset.Now).Date == DateTime.Today;
        if (AutoRefresh && isToday)
            await RefreshAsync();
    }
}
