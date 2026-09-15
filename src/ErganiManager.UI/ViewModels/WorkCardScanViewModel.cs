using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.LocalCache;

namespace ErganiManager.UI.ViewModels;

public class ScanResultRow
{
    public DateTime ScannedAt    { get; init; }
    public string EmployeeName   { get; init; } = string.Empty;
    public string MovementType   { get; init; } = string.Empty;
    public bool   Success        { get; init; }
    public string Protocol       { get; init; } = string.Empty;
    public string ErrorDescription { get; init; } = string.Empty;

    public string TimeText     => ScannedAt.ToString("HH:mm:ss");
    public string MovementIcon => MovementType == "Arrival" ? "🟢" : "🔴";
    public string StatusIcon   => Success ? "✅" : "❌";
    public string StatusText   => Success
        ? $"Protocol: {Protocol}"
        : ErrorDescription;
}

public partial class WorkCardScanViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IWorkCardSubmitter     _workCardSubmitter;
    private readonly IConnectionStateService _connectionState;
    private UserSession? _session;

    public ObservableCollection<ScanResultRow> RecentScans { get; } = new();

    [ObservableProperty] private bool   _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _barcodeInput     = string.Empty;
    [ObservableProperty] private bool   _isArrival        = true;
    [ObservableProperty] private bool   _autoDetect       = true;
    [ObservableProperty] private bool   _isProcessing;

    // Manual date/time override (only used when AutoDetect = false)
    [ObservableProperty] private DateTimeOffset? _movementDate = DateTimeOffset.Now;
    [ObservableProperty] private TimeSpan?        _movementTime = DateTime.Now.TimeOfDay;

    // Response
    [ObservableProperty] private string _responseTitle  = string.Empty;
    [ObservableProperty] private string _responseDetail = string.Empty;
    [ObservableProperty] private bool   _responseSuccess;
    [ObservableProperty] private bool   _hasResponse;

    public WorkCardScanViewModel(
        IWorkCardSubmitter workCardSubmitter,
        IConnectionStateService connectionState)
    {
        _workCardSubmitter = workCardSubmitter;
        _connectionState   = connectionState;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue
            ? string.Empty
            : "Select a company first.";
    }

    private readonly Dictionary<string, DateTime> _lastScanTime = new();
    private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(20);

    [RelayCommand]
    private async Task SubmitScanAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        var barcode = BarcodeInput.Trim();
        if (string.IsNullOrEmpty(barcode)) return;

        // Cooldown check
        if (_lastScanTime.TryGetValue(barcode, out var lastTime))
        {
            var elapsed = DateTime.Now - lastTime;
            if (elapsed < ScanCooldown)
            {
                var remaining = (int)Math.Ceiling((ScanCooldown - elapsed).TotalSeconds);
                ShowResponse(false, "⏳ Already Scanned",
                    $"This badge was scanned {(int)elapsed.TotalSeconds}s ago.\n" +
                    $"Please wait {remaining} more second(s).");
                BarcodeInput = string.Empty;
                return;
            }
        }

        IsProcessing = true;
        HasResponse  = false;

        try
        {
            // Resolve employee from local cache
            using var cache = LocalCacheDbContextFactory.Create();
            var employee = cache.CachedEmployees
                .FirstOrDefault(e => e.CompanyId == companyId
                                  && e.BarcodeId  == barcode
                                  && e.IsActive);

            if (employee == null)
            {
                ShowResponse(false, "Unknown Badge",
                    $"No active employee found with barcode '{barcode}'.");
                return;
            }

            // Determine movement type
            string movement;
            DateTime scanTime;

            if (AutoDetect)
            {
                var lastPending = cache.PendingSubmissions
                    .Where(p => p.EmployeeId == employee.Id)
                    .OrderByDescending(p => p.ScannedAt)
                    .FirstOrDefault();
                movement = lastPending?.MovementType == "Arrival" ? "Departure" : "Arrival";
                scanTime = DateTime.Now;
            }
            else
            {
                movement = IsArrival ? "Arrival" : "Departure";
                var date = (MovementDate ?? DateTimeOffset.Now).LocalDateTime.Date;
                var time = MovementTime ?? DateTime.Now.TimeOfDay;
                scanTime = date + time;
            }

            // Submit
            var result = await _workCardSubmitter.SubmitAsync(new WorkCardSubmissionRequest
            {
                EmployeeId       = employee.Id,
                CompanyId        = companyId,
                BranchId         = employee.BranchId,
                MovementType     = movement,
                MovementDateTime = scanTime
            });

            var name = employee.FullName;

            if (result.Success)
            {
                ShowResponse(true,
                    $"✅ {movement.ToUpper()} — {name}",
                    $"Protocol:      {result.Protocol}\n" +
                    $"Submission ID: {result.SubmissionId}\n" +
                    $"Time:          {scanTime:HH:mm:ss dd/MM/yyyy}");

                RecentScans.Insert(0, new ScanResultRow
                {
                    ScannedAt    = scanTime,
                    EmployeeName = name,
                    MovementType = movement,
                    Success      = true,
                    Protocol     = result.Protocol ?? string.Empty
                });
            }
            else
            {
                ShowResponse(false,
                    $"❌ Failed — {name}",
                    result.ErrorMessage
                        ?? "Ergani unavailable. Scan queued for automatic retry.");

                RecentScans.Insert(0, new ScanResultRow
                {
                    ScannedAt        = scanTime,
                    EmployeeName     = name,
                    MovementType     = movement,
                    Success          = false,
                    ErrorDescription = result.ErrorMessage ?? "Queued"
                });
            }

            while (RecentScans.Count > 50) RecentScans.RemoveAt(RecentScans.Count - 1);

            _lastScanTime[barcode] = DateTime.Now;
            BarcodeInput = string.Empty;
        }
        catch (Exception ex)
        {
            ShowResponse(false, "Error", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void ShowResponse(bool success, string title, string detail)
    {
        ResponseSuccess = success;
        ResponseTitle   = title;
        ResponseDetail  = detail;
        HasResponse     = true;
    }

    [RelayCommand]
    private void ClearResponse()
    {
        HasResponse  = false;
        BarcodeInput = string.Empty;
    }

    [RelayCommand]
    private void ClearHistory() => RecentScans.Clear();
}
