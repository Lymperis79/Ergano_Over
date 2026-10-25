using ErganiManager.Data.Entities;

namespace ErganiManager.Core.Models;

/// <summary>One place that converts between the stored role and the role used by the application.</summary>
public static class RoleMapping
{
    public static AppUserRole ToAppRole(this UserRole role) => role switch
    {
        UserRole.Admin => AppUserRole.Admin,
        UserRole.Limited => AppUserRole.Limited,
        _ => AppUserRole.Operator
    };

    public static UserRole ToEntityRole(this AppUserRole role) => role switch
    {
        AppUserRole.Admin => UserRole.Admin,
        AppUserRole.Limited => UserRole.Limited,
        _ => UserRole.Operator
    };

    /// <summary>Reads the role text stored in the offline cache ("Admin", "Operator", "Limited").</summary>
    public static AppUserRole FromCachedText(string? text) => text switch
    {
        "Admin" => AppUserRole.Admin,
        "Limited" or "Restricted" => AppUserRole.Limited, // "Restricted" = earlier name
        _ => AppUserRole.Operator
    };
}
