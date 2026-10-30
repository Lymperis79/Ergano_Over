using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.UI.Services;

/// <summary>Read-only daily attendance checks. Reports are persisted as JSON outside the database.</summary>
public sealed class DailyReconciliationService
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ErganiManager", "Reconciliation");
    private readonly ErganiManager.Core.Interfaces.IConnectionStateService _connection;
    private readonly ILogger<DailyReconciliationService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DailyReconciliationService(ErganiManager.Core.Interfaces.IConnectionStateService connection,
        ILogger<DailyReconciliationService> logger)
    { _connection = connection; _logger = logger; }

    public void Start()
    {
        if (_loop != null) return;
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        try { if (_loop != null) await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose(); _cts = null; _loop = null;
    }

    public DailyReconciliationReport? GetLatestReport(int companyId)
    {
        if (!Directory.Exists(Folder)) return null;
        var path = Directory.GetFiles(Folder, $"company-{companyId}-*.json")
            .OrderByDescending(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (path == null) return null;
        try { return JsonSerializer.Deserialize<DailyReconciliationReport>(File.ReadAllText(path)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not read latest reconciliation report."); return null; }
    }

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            while (true)
            {
                await ReconcileYesterdayIfDueAsync(token).ConfigureAwait(false);
                await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Daily reconciliation scheduler stopped."); }
    }

    private async Task ReconcileYesterdayIfDueAsync(CancellationToken token)
    {
        if (!_connection.ConfigExists()) return;
        var date = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        if (!await _gate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            await using var db = new AppDbContext(_connection.GetDbOptions());
            var companies = await db.Companies.AsNoTracking().Where(c => c.IsActive)
                .Select(c => new { c.Id, c.Name }).ToListAsync(token).ConfigureAwait(false);
            foreach (var company in companies)
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(Folder, $"company-{company.Id}-{date:yyyyMMdd}.json");
                if (File.Exists(path)) continue;
                var report = await BuildReportAsync(db, company.Id, company.Name, date, token)
                    .ConfigureAwait(false);
                Directory.CreateDirectory(Folder);
                var temp = path + ".tmp";
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
                File.Move(temp, path, true);
                _logger.LogInformation("Reconciliation completed for {Company}, {Date}: {Count} findings.",
                    company.Name, date, report.Findings.Count);
            }
        }
        finally { _gate.Release(); }
    }

    private static async Task<DailyReconciliationReport> BuildReportAsync(
        AppDbContext db, int companyId, string companyName, DateOnly date, CancellationToken token)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var cards = await db.WorkCards.AsNoTracking()
            .Where(w => w.Employee != null && w.Employee.CompanyId == companyId &&
                        w.MovementDateTime >= start.AddHours(-12) && w.MovementDateTime < end.AddHours(12) &&
                        !w.WasBlockedEarlyAttempt)
            .OrderBy(w => w.EmployeeId).ThenBy(w => w.MovementDateTime)
            .ToListAsync(token).ConfigureAwait(false);
        var names = await db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, token).ConfigureAwait(false);
        var report = new DailyReconciliationReport { CompanyId = companyId, CompanyName = companyName,
            WorkDate = date, GeneratedAtLocal = DateTime.Now };

        void Add(string type, string severity, int? employeeId, string detail, string reference)
        {
            report.Findings.Add(new DailyReconciliationFinding { Type = type, Severity = severity,
                EmployeeId = employeeId, EmployeeName = employeeId.HasValue && names.TryGetValue(employeeId.Value, out var n) ? n : "—",
                Detail = detail, Reference = reference });
        }

        var dayCards = cards.Where(c => c.MovementDateTime >= start && c.MovementDateTime < end).ToList();
        report.ScanCount = dayCards.Count;
        foreach (var group in cards.GroupBy(c => c.EmployeeId))
        {
            WorkCard? arrival = null;
            foreach (var card in group.OrderBy(c => c.MovementDateTime)
                         .Where(c => c.MovementDateTime >= start && c.MovementDateTime < end.AddHours(12)))
            {
                if (card.MovementType == MovementType.Arrival)
                {
                    if (card.MovementDateTime >= end) break;
                    if (arrival != null) Add("Missing clock-out", "Warning", group.Key,
                        $"Arrival at {arrival.MovementDateTime:HH:mm} has no departure before the next arrival.", $"WorkCard #{arrival.Id}");
                    arrival = card;
                }
                else if (arrival == null)
                {
                    if (card.MovementDateTime < end)
                        Add("Unmatched departure", "Review", group.Key,
                            $"Departure at {card.MovementDateTime:HH:mm} has no matching arrival.", $"WorkCard #{card.Id}");
                }
                else arrival = null;
            }
            if (arrival != null) Add("Missing clock-out", "Warning", group.Key,
                $"Arrival at {arrival.MovementDateTime:HH:mm} has no matching departure.", $"WorkCard #{arrival.Id}");
        }

        foreach (var card in dayCards.Where(c => !c.SubmittedToErgani))
            Add("Unsubmitted scan", "Action required", card.EmployeeId,
                $"{card.MovementType} at {card.MovementDateTime:HH:mm} is not marked as submitted.", $"WorkCard #{card.Id}");

        foreach (var group in dayCards.GroupBy(c => new { c.EmployeeId, c.MovementType }))
        {
            var ordered = group.OrderBy(c => c.MovementDateTime).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if ((ordered[i].MovementDateTime - ordered[i - 1].MovementDateTime).TotalMinutes <= 2)
                    Add("Possible duplicate scan", "Review", group.Key.EmployeeId,
                        $"Repeated {group.Key.MovementType} scans within two minutes.",
                        $"WorkCards #{ordered[i - 1].Id}, #{ordered[i].Id}");
        }

        var schedules = await db.Schedules.AsNoTracking()
            .Where(s => s.ScheduleDate == date && s.Employee != null && s.Employee.CompanyId == companyId && !s.SubmittedToErgani)
            .Select(s => new { s.Id, s.EmployeeId }).ToListAsync(token).ConfigureAwait(false);
        foreach (var s in schedules) Add("Unsubmitted schedule", "Action required", s.EmployeeId,
            "The daily schedule is not marked as submitted to Ergani.", $"Schedule #{s.Id}");

        var failed = await db.ApiSubmissionLogs.AsNoTracking()
            .Where(l => l.CompanyId == companyId && !l.Success && l.SubmissionDate >= start && l.SubmissionDate < end)
            .Select(l => new { l.Id, l.SubmissionType, l.ErrorMessage, l.EmployeeId }).ToListAsync(token).ConfigureAwait(false);
        report.FailedSubmissions = failed.Count;
        foreach (var f in failed) Add("Failed Ergani submission", "Action required", f.EmployeeId,
            $"{f.SubmissionType}: {f.ErrorMessage ?? "No error detail recorded."}", $"API log #{f.Id}");

        var overtime = await db.Overtimes.AsNoTracking()
            .Where(o => o.Employee != null && o.Employee.CompanyId == companyId && o.OvertimeDate == date &&
                        !o.IsCancelled && !o.SubmittedToErgani)
            .Select(o => new { o.Id, o.EmployeeId }).ToListAsync(token).ConfigureAwait(false);
        foreach (var o in overtime) Add("Unsubmitted overtime", "Action required", o.EmployeeId,
            "Overtime is not marked as submitted to Ergani.", $"Overtime #{o.Id}");

        var leaves = await db.Leaves.AsNoTracking()
            .Where(l => l.Employee != null && l.Employee.CompanyId == companyId && l.LeaveDate == date && !l.SubmittedToErgani)
            .Select(l => new { l.Id, l.EmployeeId }).ToListAsync(token).ConfigureAwait(false);
        foreach (var l in leaves) Add("Unsubmitted leave", "Action required", l.EmployeeId,
            "Leave is not marked as submitted to Ergani.", $"Leave #{l.Id}");

        report.UnsubmittedScans = dayCards.Count(c => !c.SubmittedToErgani);
        return report;
    }
}

public sealed class DailyReconciliationReport
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public DateOnly WorkDate { get; set; }
    public DateTime GeneratedAtLocal { get; set; }
    public int ScanCount { get; set; }
    public int UnsubmittedScans { get; set; }
    public int FailedSubmissions { get; set; }
    public System.Collections.Generic.List<DailyReconciliationFinding> Findings { get; set; } = new();
}
public sealed class DailyReconciliationFinding
{
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Reference { get; set; } = "";
}
