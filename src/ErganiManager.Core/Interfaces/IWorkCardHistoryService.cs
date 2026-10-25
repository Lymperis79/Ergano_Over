using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ErganiManager.Core.Interfaces;

public class WorkCardHistoryDto : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeFullName { get; set; } = string.Empty;
    public string EmployeeTaxId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty; // "Arrival" / "Departure"
    public DateTime MovementDateTime { get; set; }
    private bool _submittedToErgani;
    private string? _protocol;
    private bool _isLocalPending;
    private bool _isLocalFailed;
    private bool _isSelected;
    private int _retryAttempts;
    private string? _retryError;

    public bool SubmittedToErgani
    {
        get => _submittedToErgani;
        set { if (_submittedToErgani == value) return; _submittedToErgani = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanRetry)); }
    }

    public string? Protocol
    {
        get => _protocol;
        set { if (_protocol == value) return; _protocol = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }
    public string? SubmissionId { get; set; }
    public string? ResponseRawJson { get; set; }
    public DateOnly SubmissionDate { get; set; }
    public bool WasEarlyDeparture { get; set; }
    public int? EarlyDepartureMinutes { get; set; }
    public bool EmailAlertSent { get; set; }
    public DateTime CreatedAt { get; set; }

    // Local/offline queue state. These fields are populated only for scans that
    // have not yet become a normal WorkCard row in the main database.
    public int? LocalPendingId { get; set; }
    public int? LocalFailedId { get; set; }
    public bool IsLocalPending
    {
        get => _isLocalPending;
        set { if (_isLocalPending == value) return; _isLocalPending = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanRetry)); }
    }

    public bool IsLocalFailed
    {
        get => _isLocalFailed;
        set { if (_isLocalFailed == value) return; _isLocalFailed = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanRetry)); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public int RetryAttempts
    {
        get => _retryAttempts;
        set { if (_retryAttempts == value) return; _retryAttempts = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public string? RetryError
    {
        get => _retryError;
        set { if (_retryError == value) return; _retryError = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public bool CanRetry => IsLocalPending || IsLocalFailed || (!SubmittedToErgani && Id > 0);

    public string StatusText =>
        IsLocalPending ? (RetryAttempts > 0
            ? $"⏳ Retrying — attempt {RetryAttempts}" + (string.IsNullOrWhiteSpace(RetryError) ? string.Empty : $" ({RetryError})")
            : "⏳ Waiting to send") :
        IsLocalFailed ? "❌ Failed — retry pending" :
        SubmittedToErgani ? $"✅ Sent{(string.IsNullOrWhiteSpace(Protocol) ? string.Empty : $" — Protocol: {Protocol}")}" :
        "❌ Not submitted";
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class WorkCardHistoryFilter
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int? EmployeeId { get; set; }
    public int? BranchId { get; set; }
    public string? MovementType { get; set; } // null = both
    public bool? EarlyDepartureOnly { get; set; }
}

public interface IWorkCardHistoryService
{
    Task<List<WorkCardHistoryDto>> GetAsync(int companyId, WorkCardHistoryFilter filter);
}
