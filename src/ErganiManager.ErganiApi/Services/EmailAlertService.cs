using System.Text;
using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.LocalCache;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace ErganiManager.ErganiApi.Services;

public class EmailAlertService : IEmailAlertService
{
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _credentialProtector;
    private readonly ILogger<EmailAlertService> _logger;

    public EmailAlertService(
        IConnectionStateService connectionState,
        ICredentialProtector credentialProtector,
        ILogger<EmailAlertService> logger)
    {
        _connectionState = connectionState;
        _credentialProtector = credentialProtector;
        _logger = logger;
    }

    /// <summary>Sends a real test email using unsaved settings from the company form.</summary>
    public async Task<(bool Success, string? ErrorMessage)> TestSmtpAsync(EmailSmtpTestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return (false, "SMTP host is required.");
        if (string.IsNullOrWhiteSpace(request.Recipients))
            return (false, "Enter at least one recipient email address.");
        if (!string.IsNullOrWhiteSpace(request.Username) && string.IsNullOrEmpty(request.Password))
            return (false, "Enter the SMTP password to test authenticated SMTP settings.");

        var company = new Company
        {
            Name = "Ergani Manager SMTP test",
            SmtpHost = request.Host.Trim(),
            SmtpPort = request.Port,
            SmtpUser = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            SmtpPasswordEncrypted = null,
            SmtpUseTls = request.UseTls,
            AlertEmailRecipients = request.Recipients.Trim()
        };

        var subject = "Ergani Manager — SMTP test";
        var body = "This is a test email from Ergani Manager. If you received it, the SMTP settings are working.";
        var (success, error, _) = await TrySendAsync(company, subject, body, request.Password);
        if (success)
            _logger.LogInformation("SMTP test email sent successfully to {Recipients}.", request.Recipients);
        else
            _logger.LogWarning("SMTP test email failed: {Error}", error);
        return (success, error);
    }

    // ------------------------------------------------------------------ early departure

    public Task<(bool Success, string? ErrorMessage)> SendEarlyDepartureAlertAsync(
        int companyId, EarlyDepartureAlertRequest r)
    {
        var alert = new AlertMessage
        {
            Event = AlertEvent.EarlyDeparture,
            Title = ErganiManager.Data.UiText.F("Early departure – {0} [{1}]", r.EmployeeFullName, r.BranchName),
            OccurredAt = r.ActualDeparture
        }
        .Add("Branch", r.BranchName)
        .Add("Employee", string.IsNullOrWhiteSpace(r.EmployeeTaxId)
            ? r.EmployeeFullName : $"{r.EmployeeFullName} (AFM: {r.EmployeeTaxId})")
        .Add("Scheduled end", r.ScheduledEndTime.ToString("HH:mm"))
        .Add("Actual departure", r.ActualDeparture.ToString("HH:mm"))
        .Add("Difference", ErganiManager.Data.UiText.F("{0} minute(s) early", r.EarlyMinutes))
        .Add("Protocol", r.Protocol);

        return SendAlertAsync(companyId, alert);
    }

    // ------------------------------------------------------------------ generic alerts

    public async Task<(bool Success, string? ErrorMessage)> SendAlertAsync(int companyId, AlertMessage alert)
    {
        try
        {
            var company = await LoadCompanyAsync(companyId);
            if (company == null)
                return (false, $"Company {companyId} not found.");

            // Alerts off, or this kind of event not selected: silently skip, not a failure.
            if (!company.AlertEmailEnabled || (company.AlertEvents & (int)alert.Event) == 0)
                return (true, null);

            var (subject, body) = Compose(company, alert, delayedSince: null);
            var (success, error, retryable) = await TrySendAsync(company, subject, body);

            // Every mail is written to the email log (content, recipients, result).
            var logId = await WriteLogAsync(company, alert, subject, body,
                success ? "Sent" : retryable ? "Queued" : "Failed", error);

            if (!success && retryable)
            {
                // The mail server could not be reached (usually: no internet). Keep the mail and send
                // it as soon as the connection is back.
                AlertOutbox.Add(new AlertOutbox.Entry(companyId, (int)alert.Event, alert.Title, subject, body, DateTime.UtcNow, logId));
                _logger.LogWarning("Alert '{Title}' could not be sent ({Error}); it will be sent later.", alert.Title, error);
            }
            else if (!success)
            {
                _logger.LogWarning("Alert '{Title}' not sent: {Error}", alert.Title, error);
            }
            else
            {
                _logger.LogInformation("Alert '{Title}' sent.", alert.Title);
            }

            return (success, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert '{Title}'.", alert.Title);
            return (false, ex.Message);
        }
    }

    public async Task FlushOutboxAsync(int companyId)
    {
        try
        {
            var waiting = AlertOutbox.Take(companyId);
            if (waiting.Count == 0) return;

            var company = await LoadCompanyAsync(companyId);
            if (company == null) return;

            foreach (var entry in waiting)
            {
                var queuedAt = DateTime.SpecifyKind(entry.QueuedAtUtc, DateTimeKind.Utc).ToLocalTime();
                var body = entry.Body + Environment.NewLine +
                           ErganiManager.Data.UiText.F("(Delayed: this alert was created on {0:dd/MM/yyyy HH:mm} but could not be sent until now.)", queuedAt);

                var (success, error, retryable) = await TrySendAsync(company, entry.Subject, body);
                if (success)
                {
                    _logger.LogInformation("Delayed alert '{Title}' sent.", entry.Title);
                    await UpdateLogAsync(entry.LogId, "Sent", null);
                }
                else if (retryable)
                {
                    await UpdateLogAsync(entry.LogId, "Queued", error);
                    AlertOutbox.Add(entry);       // still no connection: keep it for the next round
                    _logger.LogDebug("Delayed alert '{Title}' still cannot be sent: {Error}", entry.Title, error);
                    // The mail server is not reachable: the remaining mails would fail as well.
                    foreach (var rest in waiting.SkipWhile(e => !ReferenceEquals(e, entry)).Skip(1))
                        AlertOutbox.Add(rest);
                    return;
                }
                else
                {
                    _logger.LogWarning("Delayed alert '{Title}' dropped: {Error}", entry.Title, error);
                    await UpdateLogAsync(entry.LogId, "Failed", error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send the delayed alert mails of company {Id}.", companyId);
        }
    }

    // ------------------------------------------------------------------ helpers

    private AppDbContext? OpenDb()
    {
        var config = _connectionState.LoadConfig();
        if (config == null) return null;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);
        return new AppDbContext(optionsBuilder.Options);
    }

    private static string? Trunc(string? text, int max) =>
        text != null && text.Length > max ? text[..max] : text;

    /// <summary>Saves a row in the email log. Never throws: a mail must not fail because logging did.</summary>
    private async Task<int?> WriteLogAsync(Company company, AlertMessage alert, string subject, string body,
        string status, string? error)
    {
        try
        {
            var db = OpenDb();
            if (db == null) return null;
            await using (db)
            {
                var row = new EmailLog
                {
                    CompanyId = company.Id,
                    EventCode = (int)alert.Event,
                    Title = Trunc(alert.Title, 300) ?? string.Empty,
                    Subject = Trunc(subject, 300) ?? string.Empty,
                    Recipients = Trunc(company.AlertEmailRecipients, 500),
                    Body = body,
                    Status = status,
                    ErrorMessage = Trunc(error, 1000),
                    Attempts = 1,
                    CreatedAt = DateTime.UtcNow,
                    SentAt = status == "Sent" ? DateTime.UtcNow : null
                };
                db.EmailLogs.Add(row);
                await db.SaveChangesAsync();
                return row.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the email log.");
            return null;
        }
    }

    /// <summary>Updates the log row of a mail that was kept for later.</summary>
    private async Task UpdateLogAsync(int? logId, string status, string? error)
    {
        if (logId is not int id) return;
        try
        {
            var db = OpenDb();
            if (db == null) return;
            await using (db)
            {
                var row = await db.EmailLogs.FindAsync(id);
                if (row == null) return;
                row.Status = status;
                row.ErrorMessage = Trunc(error, 1000);
                row.Attempts++;
                if (status == "Sent") row.SentAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update the email log.");
        }
    }

    private async Task<Company?> LoadCompanyAsync(int companyId)
    {
        var config = _connectionState.LoadConfig();
        if (config == null) return null;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);
        await using var db = new AppDbContext(optionsBuilder.Options);
        return await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
    }

    private async Task<(bool Success, string? Error, bool Retryable)> TrySendAsync(
        Company company, string subject, string body, string? passwordOverride = null)
    {
        if (string.IsNullOrWhiteSpace(company.AlertEmailRecipients) || string.IsNullOrWhiteSpace(company.SmtpHost))
            return (false, "Email alerts are enabled but SMTP host or recipients are not configured.", false);

        var recipients = company.AlertEmailRecipients
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (recipients.Count == 0)
            return (false, "No recipient email addresses configured.", false);

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(string.IsNullOrWhiteSpace(company.SmtpUser) ? recipients[0] : company.SmtpUser));
            foreach (var address in recipients) message.To.Add(MailboxAddress.Parse(address));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient { Timeout = 30_000 };

            await client.ConnectAsync(company.SmtpHost, company.SmtpPort ?? 587,
                company.SmtpUseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

            if (!string.IsNullOrWhiteSpace(company.SmtpUser))
            {
                var password = passwordOverride ?? (string.IsNullOrEmpty(company.SmtpPasswordEncrypted)
                    ? string.Empty
                    : _credentialProtector.Unprotect(company.SmtpPasswordEncrypted));
                if (!string.IsNullOrEmpty(password))
                    await client.AuthenticateAsync(company.SmtpUser, password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
            return (true, null, false);
        }
        catch (Exception ex)
        {
            // Wrong password or a rejected address will not fix itself; everything else
            // (connection, timeout, DNS) is worth trying again later.
            var permanent = ex is AuthenticationException || ex is FormatException || ex is ParseException;
            return (false, ex.Message, !permanent);
        }
    }

    private static (string Subject, string Body) Compose(Company company, AlertMessage alert, DateTime? delayedSince)
    {
        var line = new string('─', 41);
        var sb = new StringBuilder();
        sb.AppendLine(alert.Title.ToUpperInvariant());
        sb.AppendLine(line);
        sb.AppendLine($"{ErganiManager.Data.UiText.T("Company") + ":",-26} {company.Name}");
        foreach (var (label, value) in alert.Lines)
            sb.AppendLine($"{label + ":",-26} {value}");
        sb.AppendLine($"{ErganiManager.Data.UiText.T("Time") + ":",-26} {alert.OccurredAt:dddd, d MMMM yyyy HH:mm}");
        sb.AppendLine(line);
        sb.Append(ErganiManager.Data.UiText.T("This alert was generated automatically by Ergani Manager."));
        return ($"⚠️ {alert.Title}", sb.ToString());
    }
}

/// <summary>
/// Alert mails that could not be delivered yet, kept in a small file next to the local cache so they
/// survive a restart and are sent once the connection is back.
/// </summary>
internal static class AlertOutbox
{
    public sealed record Entry(int CompanyId, int Event, string Title, string Subject, string Body, DateTime QueuedAtUtc, int? LogId = null);

    private const int MaxEntries = 200;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly object Gate = new();

    private static string FilePath => Path.Combine(AppPaths.GetAppDataFolder(), "alert-outbox.json");

    private static List<Entry> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new List<Entry>()
                : new List<Entry>();
        }
        catch { return new List<Entry>(); }
    }

    private static void Save(List<Entry> entries)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(entries)); }
        catch { /* a mail that cannot be remembered is lost; never break the app for it */ }
    }

    public static void Add(Entry entry)
    {
        lock (Gate)
        {
            var all = Load();
            all.Add(entry);
            Save(all.OrderBy(e => e.QueuedAtUtc).TakeLast(MaxEntries).ToList());
        }
    }

    /// <summary>Removes and returns the waiting mails of a company (too old ones are dropped).</summary>
    public static List<Entry> Take(int companyId)
    {
        lock (Gate)
        {
            var all = Load().Where(e => DateTime.UtcNow - e.QueuedAtUtc < MaxAge).ToList();
            var mine = all.Where(e => e.CompanyId == companyId).OrderBy(e => e.QueuedAtUtc).ToList();
            if (mine.Count > 0) Save(all.Where(e => e.CompanyId != companyId).ToList());
            return mine;
        }
    }
}
