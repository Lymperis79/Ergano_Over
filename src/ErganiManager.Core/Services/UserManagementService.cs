using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class UserManagementService : IUserManagementService
{
    private readonly IConnectionStateService _connectionState;
    private readonly IPasswordHasher _passwordHasher;

    public UserManagementService(IConnectionStateService connectionState, IPasswordHasher passwordHasher)
    {
        _connectionState = connectionState;
        _passwordHasher = passwordHasher;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<AppUserDto>> GetByCompanyAsync(int companyId)
    {
        await using var db = OpenDb();
        var users = await db.Users
            .Include(u => u.Company)
            .Include(u => u.Branch)
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.Username)
            .ToListAsync();

        return users.Select(ToDto).ToList();
    }

    public async Task<AppUserDto?> GetByIdAsync(int id)
    {
        await using var db = OpenDb();
        var user = await db.Users
            .Include(u => u.Company)
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Id == id);

        return user == null ? null : ToDto(user);
    }

    public async Task<int> CreateAsync(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Username is required."));

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Password must be at least 8 characters."));

        // An Operator without a company would never be able to clock anyone
        // in (the terminal scopes everything by ActiveCompanyId), so guard
        // against that configuration mistake here rather than downstream.
        if (request.Role == AppUserRole.Operator && request.CompanyId == null)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Operator accounts must be assigned to a company."));

        // A limited user belongs to one company and must be given at least one menu.
        var allowedMenus = request.AllowedMenus;
        if (request.Role == AppUserRole.Limited)
        {
            if (request.CompanyId == null)
                throw new InvalidOperationException(ErganiManager.Data.UiText.T("Limited accounts must be assigned to a company."));
            allowedMenus = SanitizeLimitedMenus(request.AllowedMenus)
                ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("Select at least one menu for a limited user."));
        }

        await using var db = OpenDb();

        var usernameTaken = await db.Users.AnyAsync(u => u.Username == request.Username.Trim());
        if (usernameTaken)
            throw new InvalidOperationException(ErganiManager.Data.UiText.F("Username '{0}' is already taken.", request.Username));

        var entity = new AppUser
        {
            Username = request.Username.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = request.Role.ToEntityRole(),
            CompanyId = request.CompanyId,
            BranchId = request.BranchId,
            AllowedMenus = allowedMenus,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task ResetPasswordAsync(int userId, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Password must be at least 8 characters."));

        await using var db = OpenDb();
        var user = await db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.F("User {0} not found.", userId));

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
    }

    public async Task UpdateMenusAsync(int userId, string? allowedMenus)
    {
        await using var db = OpenDb();
        var user = await db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.F("User {0} not found.", userId));

        if (user.Role == UserRole.Limited)
            allowedMenus = SanitizeLimitedMenus(allowedMenus)
                ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("Select at least one menu for a limited user."));

        user.AllowedMenus = allowedMenus;
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int userId, bool isActive)
    {
        await using var db = OpenDb();
        var user = await db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.F("User {0} not found.", userId));

        user.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task UpdateRoleAndAssignmentAsync(int userId, AppUserRole role, int? companyId, int? branchId)
    {
        if (role == AppUserRole.Operator && companyId == null)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Operator accounts must be assigned to a company."));

        await using var db = OpenDb();
        var user = await db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.F("User {0} not found.", userId));

        if (role == AppUserRole.Limited && companyId == null)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Limited accounts must be assigned to a company."));

        user.Role = role.ToEntityRole();
        user.CompanyId = companyId;
        user.BranchId = branchId;

        // A limited user with no menu list would see nothing: start with Help until menus are chosen.
        if (role == AppUserRole.Limited)
            user.AllowedMenus = SanitizeLimitedMenus(user.AllowedMenus) ?? MenuKeys.Help;
        await db.SaveChangesAsync();
    }

    /// <summary>Keeps only menus a limited user may have (never Companies or Users). Null when none remain.</summary>
    private static string? SanitizeLimitedMenus(string? stored)
    {
        var keys = MenuKeys.Parse(stored)?
            .Where(k => k != MenuKeys.Companies && k != MenuKeys.Users).ToList();
        return keys == null || keys.Count == 0 ? null : MenuKeys.Serialize(keys);
    }

    private static AppUserDto ToDto(AppUser u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Role = u.Role.ToAppRole(),
        CompanyId = u.CompanyId,
        CompanyName = u.Company?.Name,
        BranchId = u.BranchId,
        BranchName = u.Branch?.Name,
        IsActive = u.IsActive,
        LastLoginAt = u.LastLoginAt,
        AllowedMenus = u.AllowedMenus
    };

    public async Task DeleteAsync(int userId)
    {
        await using var db = OpenDb();
        var entity = await db.Users.FindAsync(userId);
        if (entity != null)
        {
            db.Users.Remove(entity);
            await db.SaveChangesAsync();
        }
    }
}