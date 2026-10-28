using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.UI.ViewModels;

/// <summary>One line of the email log.</summary>
public sealed class EmailLogRow
{
    public int Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? SentAt { get; init; }
    public int EventCode { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Recipients { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string Body { get; init; } = string.Empty;
    public int Attempts { get; init; }

    private static string Local(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public string TimeText => Local(CreatedAt);

    public string EventText
    {
        get
        {
            var key = $"Vm.Mail event {EventCode}";
            var text = Tr.Loc[key];
            return string.IsNullOrWhiteSpace(text) || text == key ? string.Empty : text;
        }
    }

    public string StatusIcon => Status switch { "Sent" => "✅", "Queued" => "⏳", _ => "❌" };

    public string StatusText => Status switch
    {
        "Sent"   => Tr.Loc["Vm.Mail sent"],
        "Queued" => Tr.Loc["Vm.Mail queued"],
        _        => Tr.Loc["Vm.Mail failed"]
    };

    /// <summary>Everything about the mail in one readable block (shown when the row is selected).</summary>
    public string DetailsText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Tr.Loc["Vm.Mail subject"]}: {Subject}");
            sb.AppendLine($"{Tr.Loc["Vm.Mail recipients"]}: {Recipients}");
            sb.AppendLine($"{Tr.Loc["Vm.Mail status"]}: {StatusIcon} {StatusText}");
            sb.AppendLine($"{Tr.Loc["Vm.Mail created"]}: {TimeText}");
            if (SentAt != null) sb.AppendLine($"{Tr.Loc["Vm.Mail sent at"]}: {Local(SentAt.Value)}");
            sb.AppendLine($"{Tr.Loc["Vm.Mail attempts"]}: {Attempts}");
            if (!string.IsNullOrWhiteSpace(ErrorMessage)) sb.AppendLine($"{Tr.Loc["Vm.Mail error"]}: {ErrorMessage}");
            sb.AppendLine(new string('─', 60));
            sb.Append(Body);
            return sb.ToString();
        }
    }
}

/// <summary>Shows the alert mails the application sent (or tried to send), with their full content.</summary>
public partial class EmailLogViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IConnectionStateService _connectionState;
    private UserSession? _session;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _autoRefreshCts;
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(5);

    public ObservableCollection<EmailLogRow> Rows { get; } = new();

    /// <summary>The choices of the status filter (same order as <see cref="StatusFilter"/>).</summary>
    public ObservableCollection<string> StatusOptions { get; } = new();

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private DateTimeOffset? _fromDate = DateTimeOffset.Now.AddDays(-30);
    [ObservableProperty] private DateTimeOffset? _toDate = DateTimeOffset.Now;
    /// <summary>0 = all, 1 = sent, 2 = queued, 3 = failed.</summary>
    [ObservableProperty] private int _statusFilter;
    [ObservableProperty] private EmailLogRow? _selectedRow;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public bool HasSelection => SelectedRow != null;

    public EmailLogViewModel(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
        StatusOptions.Add(Loc["EmailStatusAll"]);
        StatusOptions.Add(Loc["Vm.Mail sent"]);
        StatusOptions.Add(Loc["Vm.Mail queued"]);
        StatusOptions.Add(Loc["Vm.Mail failed"]);
    }

    partial void OnSelectedRowChanged(EmailLogRow? value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnStatusFilterChanged(int value)
    {
        if (HasActiveCompany) _ = LoadAsync();
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue ? string.Empty : Loc["Vm.Select a company first."];
        // Loading is triggered when the Email Log section is selected, not just when the
        // cached ViewModel is first created.
    }

    /// <summary>Refresh immediately whenever the user opens/selects the Email Log section.</summary>
    public async Task RefreshOnFormSelectedAsync()
    {
        if (!HasActiveCompany) return;
        await LoadAsync();
        StartAutoRefresh();
    }

    /// <summary>Stop background polling when the user leaves the Email Log section.</summary>
    public void StopAutoRefresh()
    {
        var cts = Interlocked.Exchange(ref _autoRefreshCts, null);
        if (cts == null) return;
        cts.Cancel();
        cts.Dispose();
    }

    private void StartAutoRefresh()
    {
        if (_autoRefreshCts != null || !HasActiveCompany) return;

        var cts = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _autoRefreshCts, cts, null) != null)
        {
            cts.Dispose();
            return;
        }

        _ = AutoRefreshLoopAsync(cts);
    }

    private async Task AutoRefreshLoopAsync(CancellationTokenSource owner)
    {
        try
        {
            while (!owner.IsCancellationRequested)
            {
                await Task.Delay(AutoRefreshInterval, owner.Token);
                await LoadAsync();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.CompareExchange(ref _autoRefreshCts, null, owner);
            owner.Dispose();
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId) return;
        // Avoid overlapping refreshes from the timer, filter changes, and the Refresh button.
        if (!await _loadGate.WaitAsync(0)) return;

        var previouslySelectedId = SelectedRow?.Id;
        try
        {
            var from = DateTime.SpecifyKind((FromDate ?? DateTimeOffset.Now.AddDays(-30)).LocalDateTime.Date, DateTimeKind.Local);
            var to   = DateTime.SpecifyKind((ToDate ?? DateTimeOffset.Now).LocalDateTime.Date.AddDays(1), DateTimeKind.Local);
            var fromUtc = from.ToUniversalTime();
            var toUtc   = to.ToUniversalTime();
            var wanted  = StatusFilter switch { 1 => "Sent", 2 => "Queued", 3 => "Failed", _ => null };

            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var query = db.EmailLogs.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.CreatedAt >= fromUtc && l.CreatedAt < toUtc);
            if (wanted != null) query = query.Where(l => l.Status == wanted);

            var items = await query.OrderByDescending(l => l.CreatedAt).Take(500).ToListAsync();

            Rows.Clear();
            foreach (var l in items)
                Rows.Add(new EmailLogRow
                {
                    Id = l.Id, CreatedAt = l.CreatedAt, SentAt = l.SentAt, EventCode = l.EventCode, Status = l.Status,
                    Subject = l.Subject, Recipients = l.Recipients ?? string.Empty, ErrorMessage = l.ErrorMessage,
                    Body = l.Body, Attempts = l.Attempts
                });

            SelectedRow = previouslySelectedId is int selectedId
                ? Rows.FirstOrDefault(row => row.Id == selectedId)
                : null;
            StatusMessage = Rows.Count == 0
                ? Loc["Vm.No emails in this period."]
                : string.Format(Loc["Vm.{0} email(s) found."], Rows.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            _loadGate.Release();
        }
    }
}
