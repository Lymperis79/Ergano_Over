using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public class ReportDefinition
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SourceEntity { get; set; } = string.Empty;

    public string ColumnsJson { get; set; } = "[]";
    public string FiltersJson { get; set; } = "[]";

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsShared { get; set; } = false;
}

public class EmployeeImport
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [Required, MaxLength(300)]
    public string FileName { get; set; } = string.Empty;

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public int ImportedByUserId { get; set; }

    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }

    public string? ErrorDetailsJson { get; set; }
}
