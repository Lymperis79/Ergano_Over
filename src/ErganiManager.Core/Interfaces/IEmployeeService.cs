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
    public bool IsActive { get; set; } = true;

    public string FullName => $"{FirstName} {LastName}";
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
