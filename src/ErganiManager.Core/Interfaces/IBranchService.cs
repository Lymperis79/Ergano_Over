namespace ErganiManager.Core.Interfaces;

public class BranchDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int BranchNumber { get; set; }
    public string Address { get; set; } = string.Empty;
    public string SepeServiceCode { get; set; } = string.Empty;
    public string? OaedServiceCode { get; set; }
    public string ActivityCode { get; set; } = string.Empty;
    public string KallikratisMunicipalCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public interface IBranchService
{
    Task<List<BranchDto>> GetByCompanyAsync(int companyId);
    Task<BranchDto?> GetByIdAsync(int id);
    Task<int> CreateAsync(BranchDto dto);
    Task UpdateAsync(BranchDto dto);
    Task SetActiveAsync(int id, bool isActive);
    Task DeleteAsync(int id);
}
