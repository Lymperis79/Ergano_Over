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

    [Required, MaxLength(400)]
    public string ProfessionCode { get; set; } = string.Empty;

    public int WeeklyWorkdays { get; set; } = 5; // 5 or 6

    // Personal and employment information provided by Ergani EX_BASE_05.
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public DateTimeOffset? BirthDate { get; set; }
    public string? Sex { get; set; }
    public string? Nationality { get; set; }
    public string? MaritalStatus { get; set; }
    public int? NumberOfChildren { get; set; }
    public string? IdentityDocumentType { get; set; }
    public string? IdentityDocumentNumber { get; set; }
    public DateTimeOffset? EmploymentStartDate { get; set; }
    public string? EmploymentClassification { get; set; }
    public string? ExperienceYears { get; set; }
    public string? EmploymentRelationship { get; set; }
    public string? EmploymentStatus { get; set; }
    public double? WeeklyHours { get; set; }
    public string? WorkScheduleDescription { get; set; }
    public string? BreakDescription { get; set; }
    public string? EducationLevel { get; set; }
    public string? DigitalTimeOrganization { get; set; }
    public double? FullEmploymentHours { get; set; }
    public int? BreakMinutes { get; set; }
    public string? BreakWithinWorkingHours { get; set; }
    public string? UsesDigitalWorkCard { get; set; }
    public int? FlexibleScheduleMinutes { get; set; }
    public DateTimeOffset? LastModifiedDate { get; set; }

    /// <summary>Contractual/normal daily hours used only to flag potential extra time for review.</summary>
    public double NormalDailyHours { get; set; } = 8;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<EmployeeSchedule> Schedules { get; set; } = new();
    public List<WorkCard> WorkCards { get; set; } = new();
    public List<Overtime> Overtimes { get; set; } = new();

    public string FullName => $"{FirstName} {LastName}";
}
