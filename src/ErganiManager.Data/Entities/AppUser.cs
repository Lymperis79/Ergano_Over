using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public enum UserRole
{
    Admin = 0,
    Operator = 1,

    /// <summary>Signs in to the management screens but only sees the menus ticked for the user.
    /// Never sees Companies, Users or Administration.</summary>
    Limited = 2
}

/// <summary>Reads the stored role text. Anything unknown becomes the least privileged role (Operator).
/// "Restricted" is the name an earlier build used for Limited (also when a narrow column cut it short).</summary>
public static class UserRoleText
{
    public static UserRole Parse(string? text)
    {
        if (string.Equals(text, "Admin", StringComparison.OrdinalIgnoreCase)) return UserRole.Admin;
        if (text != null && (text.StartsWith("Limited", StringComparison.OrdinalIgnoreCase)
                             || text.StartsWith("Restrict", StringComparison.OrdinalIgnoreCase)))
            return UserRole.Limited;
        return UserRole.Operator;
    }
}

public class AppUser
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Operator;

    // Null CompanyId = super-admin who can switch between companies
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    // Operators can optionally be locked to one branch's terminal
    public int? BranchId { get; set; }
    public BusinessBranch? Branch { get; set; }

    /// <summary>Comma separated menu keys this user may see in the main window.
    /// Null = no restriction (all menus).</summary>
    [MaxLength(500)]
    public string? AllowedMenus { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
