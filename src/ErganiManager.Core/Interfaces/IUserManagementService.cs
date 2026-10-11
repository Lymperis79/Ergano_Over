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

    /// <summary>Comma separated menu keys the user may see; null = all menus.</summary>
    public string? AllowedMenus { get; set; }
    public bool HasMenuRestrictions => !string.IsNullOrWhiteSpace(AllowedMenus);
}

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public AppUserRole Role { get; set; } = AppUserRole.Operator;
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }

    /// <summary>Menus the user may see (comma separated keys); null = all menus.</summary>
    public string? AllowedMenus { get; set; }
}

public interface IUserManagementService
{
    Task<List<AppUserDto>> GetByCompanyAsync(int companyId);
    Task<AppUserDto?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreateUserRequest request);
    Task ResetPasswordAsync(int userId, string newPassword);
    Task SetActiveAsync(int userId, bool isActive);
    Task DeleteAsync(int userId);
    /// <summary>Sets which menus the user may see (null = all menus).</summary>
    Task UpdateMenusAsync(int userId, string? allowedMenus);
    Task UpdateRoleAndAssignmentAsync(int userId, AppUserRole role, int? companyId, int? branchId);
}
