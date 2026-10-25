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

    public string DetailsText => ErganiManager.Data.UiText.F("{0} · {1} · by {2}", Origin, FileName, ImportedBy);

    public string CountsText =>
        ErganiManager.Data.UiText.F("{0} employee(s) · {1} schedule day(s) · {2} overtime · {3} holiday(s)", EmployeeCount, ScheduleDays, OvertimeRecords, HolidayDays) +
        (SkippedRows > 0 ? ErganiManager.Data.UiText.F(" · {0} row(s) skipped", SkippedRows) : string.Empty);
}

/// <summary>Keeps a history of the imports of Ergani data.</summary>
public interface IImportLogService
{
    Task AddAsync(ImportLogDto entry, int companyId);

    /// <summary>The most recent imports of the company, newest first.</summary>
    Task<List<ImportLogDto>> GetRecentAsync(int companyId, int take = 50);
}
