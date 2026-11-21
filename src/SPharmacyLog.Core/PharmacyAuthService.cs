using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;

namespace SPharmacyLog.Core;

public sealed class PharmacyAuthService
{
    public async Task<bool> HasUsersAsync()
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        return await db.Users.AnyAsync();
    }

    public async Task CreateFirstAdminAsync(string name, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new InvalidOperationException("Username is required and password must contain at least 8 characters.");

        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        if (await db.Users.AnyAsync())
            throw new InvalidOperationException("An administrator already exists.");

        db.Users.Add(new PharmacyUser
        {
            Username = name.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = PharmacyRole.Admin
        });
        await db.SaveChangesAsync();
    }

    public async Task<PharmacyUser?> LoginAsync(string name, string password)
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var user = await db.Users.FirstOrDefaultAsync(
            item => item.Username == name.Trim() && item.IsActive);
        return user is not null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user : null;
    }
}
