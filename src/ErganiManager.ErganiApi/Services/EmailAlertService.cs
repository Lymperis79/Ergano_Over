using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
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

    public async Task<(bool Success, string? ErrorMessage)> SendEarlyDepartureAlertAsync(
        int companyId, EarlyDepartureAlertRequest request)
    {
        try
        {
            var config = _connectionState.LoadConfig();
            if (config == null)
                return (false, "Database not configured.");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            DbProviderFactory.Configure(optionsBuilder, config);
            await using var db = new AppDbContext(optionsBuilder.Options);

            var company = await db.Companies.FindAsync(companyId);
            if (company == null)
                return (false, $"Company {companyId} not found.");

            if (!company.AlertEmailEnabled)
                return (true, null); // alerts disabled — silently skip, not a failure

            if (string.IsNullOrWhiteSpace(company.AlertEmailRecipients) ||
                string.IsNullOrWhiteSpace(company.SmtpHost))
                return (false, "Email alerts are enabled but SMTP host or recipients are not configured.");

            var recipients = company.AlertEmailRecipients
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (recipients.Count == 0)
                return (false, "No recipient email addresses configured.");

            var message = BuildMessage(company.SmtpUser ?? company.SmtpHost, recipients, request);

            using var client = new SmtpClient();

            var secureSocketOptions = company.SmtpUseTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(company.SmtpHost, company.SmtpPort ?? 587, secureSocketOptions);

            if (!string.IsNullOrEmpty(company.SmtpUser) && !string.IsNullOrEmpty(company.SmtpPasswordEncrypted))
            {
                var smtpPassword = _credentialProtector.Unprotect(company.SmtpPasswordEncrypted);
                await client.AuthenticateAsync(company.SmtpUser, smtpPassword);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);

            _logger.LogInformation(
                "Early-departure alert sent for {Employee} ({Minutes} min early) to {Count} recipient(s).",
                request.EmployeeFullName, request.EarlyMinutes, recipients.Count);

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send early-departure alert for employee {Employee}.",
                request.EmployeeFullName);
            return (false, ex.Message);
        }
    }

    private static MimeMessage BuildMessage(string senderAddress, List<string> recipientAddresses,
        EarlyDepartureAlertRequest r)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(senderAddress));
        foreach (var addr in recipientAddresses)
            message.To.Add(MailboxAddress.Parse(addr));

        message.Subject = $"⚠️ Early departure – {r.EmployeeFullName} [{r.BranchName}]";

        var body = new TextPart("plain")
        {
            Text = $"""
                    EARLY DEPARTURE ALERT
                    ─────────────────────────────────────────
                    Company:           {r.CompanyName}
                    Branch:            {r.BranchName}
                    Employee:          {r.EmployeeFullName} (AFM: {r.EmployeeTaxId})
                    Scheduled end:     {r.ScheduledEndTime:HH:mm}
                    Actual departure:  {r.ActualDeparture:HH:mm}
                    Difference:        {r.EarlyMinutes} minute(s) early
                    Date:              {r.ActualDeparture:dddd, d MMMM yyyy}
                    Protocol:          {r.Protocol ?? "N/A"}
                    ─────────────────────────────────────────
                    This alert was generated automatically by Ergani Manager.
                    """
        };

        message.Body = body;
        return message;
    }
}
