using System.ComponentModel;

namespace ErganiManager.UI.ViewModels;

/// <summary>One line of the terminal scan list; submission status updates live.</summary>
public sealed class RecentScanItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _statusText = string.Empty;
    private bool _isOk;
    private bool _isWarn;
    private bool _isBad;

    public int PendingId { get; init; }
    public string TimeText { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = "—";
    public string MovementText { get; init; } = string.Empty;
    public bool IsArrival { get; init; }
    public bool IsDeparture { get; init; }
    public string StatusText { get => _statusText; set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(nameof(StatusText)); } }
    public bool IsOk { get => _isOk; set { if (_isOk == value) return; _isOk = value; OnPropertyChanged(nameof(IsOk)); } }
    public bool IsWarn { get => _isWarn; set { if (_isWarn == value) return; _isWarn = value; OnPropertyChanged(nameof(IsWarn)); } }
    public bool IsBad { get => _isBad; set { if (_isBad == value) return; _isBad = value; OnPropertyChanged(nameof(IsBad)); } }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
