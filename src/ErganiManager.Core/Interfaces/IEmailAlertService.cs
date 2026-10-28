namespace ErganiManager.Core.Interfaces;

public class EarlyDepartureAlertRequest
{
    public string EmployeeFullName { get; set; } = string.Empty;
    public string EmployeeTaxId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public TimeOnly ScheduledEndTime { get; set; }
    public DateTime ActualDeparture { get; set; }
    public int EarlyMinutes { get; set; }
    public string? Protocol { get; set; }
}

/// <summary>The situations a company can be e-mailed about (stored as a bit mask on the company).</summary>
[Flags]
public enum AlertEvent
{
    None              = 0,
    /// <summary>An employee clocks out earlier than the scheduled end.</summary>
    EarlyDeparture    = 1,
    /// <summary>The Ergani service does not answer although the internet works.</summary>
    ErganiDown        = 2,
    /// <summary>The internet connection was down (mailed once it is back).</summary>
    InternetDown      = 4,
    EarlyClockIn      = 8,
    LateClockIn       = 16,
    /// <summary>Clock-in on a day without a schedule, or on a rest/absence day.</summary>
    NoScheduleClockIn = 32,
    /// <summary>Ergani rejected a queued scan.</summary>
    ScanRejected      = 64,

    All = EarlyDeparture | ErganiDown | InternetDown | EarlyClockIn | LateClockIn | NoScheduleClockIn | ScanRejected,
    /// <summary>What companies had before the choice existed.</summary>
    Default = EarlyDeparture
}

/// <summary>One alert mail: a title and label/value lines.</summary>
public class AlertMessage
{
    public AlertEvent Event { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public List<(string Label, string Value)> Lines { get; } = new();

    public AlertMessage Add(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Lines.Add((ErganiManager.Data.UiText.T(label), value));
        return this;
    }
}

/// <summary>SMTP settings entered in the company form, used to send a real test message.</summary>
public sealed record EmailSmtpTestRequest(
    string Host, int Port, string? Username, string? Password,
    string Recipients, bool UseTls);

public interface IEmailAlertService
{
    /// <summary>Sends a test message using the settings currently entered in the company form.</summary>
    Task<(bool Success, string? ErrorMessage)> TestSmtpAsync(EmailSmtpTestRequest request);

    /// <summary>
    /// Sends an alert mail with the SMTP settings of the company, but only when the company has alerts
    /// enabled AND selected this kind of event. Never throws. When the mail server cannot be reached
    /// (e.g. the internet is down) the mail is kept and sent by <see cref="FlushOutboxAsync"/> later.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SendAlertAsync(int companyId, AlertMessage alert);

    /// <summary>Updates an existing queued alert (and its log row) without creating a duplicate email.</summary>
    Task<bool> UpdateQueuedAlertAsync(int companyId, AlertMessage alert);

    /// <summary>Sends the alert mails that could not be delivered earlier. Never throws.</summary>
    Task FlushOutboxAsync(int companyId);

    /// <summary>
    /// Sends an early-departure alert email using the SMTP settings stored
    /// on the company. Returns (true, null) on success or (false, errorMessage)
    /// on failure — never throws, so a failed email doesn't roll back the
    /// WorkCard submission that triggered it.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SendEarlyDepartureAlertAsync(
        int companyId, EarlyDepartureAlertRequest request);
}
