using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// One record per import of Ergani data (schedule Excel export): when it happened, who did it,
/// which file, and for which dates the imported data is.
/// </summary>
public class ErganiImportLog
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>UTC time of the import.</summary>
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string ImportedBy { get; set; } = string.Empty;

    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>"Portal" (downloaded from the Ergani portal) or "Manual" (file picked by the user).</summary>
    [MaxLength(20)]
    public string Origin { get; set; } = "Manual";

    /// <summary>First and last date contained in the imported data.</summary>
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }

    public int EmployeeCount { get; set; }
    public int ScheduleDays { get; set; }
    public int OvertimeRecords { get; set; }
    public int HolidayDays { get; set; }
    public int SkippedRows { get; set; }
}
