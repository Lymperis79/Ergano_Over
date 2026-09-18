using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public class Employee
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public int BranchId { get; set; }
    public BusinessBranch? Branch { get; set; }

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, MaxLength(9)]
    public string TaxId { get; set; } = string.Empty; // AFM

    [Required, MaxLength(11)]
    public string SocialSecurityNumber { get; set; } = string.Empty; // AMKA

    [Required, MaxLength(50)]
    public string BarcodeId { get; set; } = string.Empty; // what the scanner reads

    [Required, MaxLength(20)]
    public string ProfessionCode { get; set; } = string.Empty;

    public int WeeklyWorkdays { get; set; } = 5; // 5 or 6

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<EmployeeSchedule> Schedules { get; set; } = new();
    public List<WorkCard> WorkCards { get; set; } = new();
    public List<Overtime> Overtimes { get; set; } = new();

    public string FullName => $"{FirstName} {LastName}";
}
