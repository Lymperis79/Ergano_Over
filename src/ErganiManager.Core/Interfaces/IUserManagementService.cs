using ErganiManager.Core.Models;

namespace ErganiManager.Core.Interfaces;

public class AppUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public AppUserRole Role { get; set; }
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
}

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public AppUserRole Role { get; set; } = AppUserRole.Operator;
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }
}

public interface IUserManagementService
{
    Task<List<AppUserDto>> GetByCompanyAsync(int companyId);
    Task<AppUserDto?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreateUserRequest request);
    Task ResetPasswordAsync(int userId, string newPassword);
    Task SetActiveAsync(int userId, bool isActive);
    Task DeleteAsync(int userId);
    Task UpdateRoleAndAssignmentAsync(int userId, AppUserRole role, int? companyId, int? branchId);
}
