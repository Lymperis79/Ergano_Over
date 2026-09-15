using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public enum UserRole
{
    Admin = 0,
    Operator = 1
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

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
