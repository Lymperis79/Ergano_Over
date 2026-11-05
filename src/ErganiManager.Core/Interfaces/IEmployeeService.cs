namespace ErganiManager.Core.Interfaces;

public class EmployeeDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string SocialSecurityNumber { get; set; } = string.Empty;
    public string BarcodeId { get; set; } = string.Empty;
    public string ProfessionCode { get; set; } = string.Empty;
    public int WeeklyWorkdays { get; set; } = 5;
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
    public double NormalDailyHours { get; set; } = 8;
    public bool IsActive { get; set; } = true;

    public string FullName => $"{FirstName} {LastName}";
    public string DisplayName =>
        $"{FirstName} {LastName}" +
        (string.IsNullOrWhiteSpace(FatherName) ? string.Empty : $" {FatherName}") +
        (string.IsNullOrWhiteSpace(TaxId) ? string.Empty : $" · ΑΦΜ: {TaxId}");
}

public interface IEmployeeService
{
    Task<List<EmployeeDto>> GetByCompanyAsync(int companyId, bool activeOnly = false);
    Task<EmployeeDto?> GetByIdAsync(int id);
    Task<int> CreateAsync(EmployeeDto dto);
    Task UpdateAsync(EmployeeDto dto);
    Task SetActiveAsync(int id, bool isActive);
    Task DeleteAsync(int id);

    /// <summary>True if the barcode is already used by another active employee
    /// in the same company. Used for both manual entry validation and bulk import.</summary>
    Task<bool> IsBarcodeTakenAsync(int companyId, string barcodeId, int? excludeEmployeeId = null);
}
