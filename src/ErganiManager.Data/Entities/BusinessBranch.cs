using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public class BusinessBranch
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public int BranchNumber { get; set; } // f_aa

    [Required, MaxLength(300)]
    public string Address { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string SepeServiceCode { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? OaedServiceCode { get; set; }

    [Required, MaxLength(20)]
    public string ActivityCode { get; set; } = string.Empty; // KAD

    [Required, MaxLength(20)]
    public string KallikratisMunicipalCode { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public List<Employee> Employees { get; set; } = new();
}
