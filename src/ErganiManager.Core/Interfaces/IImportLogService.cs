namespace ErganiManager.Core.Interfaces;

public class ImportLogDto
{
    public int Id { get; set; }
    public DateTime ImportedAt { get; set; }          // UTC
    public string ImportedBy { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Origin { get; set; } = "Manual";
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int EmployeeCount { get; set; }
    public int ScheduleDays { get; set; }
    public int OvertimeRecords { get; set; }
    public int HolidayDays { get; set; }
    public int SkippedRows { get; set; }

    // Display helpers for the history list
    public string ImportedAtText => ImportedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    public string PeriodText => FromDate == ToDate
        ? FromDate.ToString("dd/MM/yyyy")
        : $"{FromDate:dd/MM/yyyy} – {ToDate:dd/MM/yyyy}";

    public string DetailsText => $"{Origin} · {FileName} · by {ImportedBy}";

    public string CountsText =>
        $"{EmployeeCount} employee(s) · {ScheduleDays} schedule day(s) · {OvertimeRecords} overtime · {HolidayDays} holiday(s)" +
        (SkippedRows > 0 ? $" · {SkippedRows} row(s) skipped" : string.Empty);
}

/// <summary>Keeps a history of the imports of Ergani data.</summary>
public interface IImportLogService
{
    Task AddAsync(ImportLogDto entry, int companyId);

    /// <summary>The most recent imports of the company, newest first.</summary>
    Task<List<ImportLogDto>> GetRecentAsync(int companyId, int take = 50);
}
