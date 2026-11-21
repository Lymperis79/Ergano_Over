using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Models;

namespace SPharmacyLog.Services;

public sealed class PharmacyAuthService
{
    public async Task<bool> HasUsersAsync()
    {
        await using var db = new PharmacyDbContext(Program.DbOptions());
        return await db.Users.AnyAsync();
    }

    public async Task CreateInitialAdminAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || password.Length < 8)
            throw new InvalidOperationException("Username is required and password must be at least 8 characters.");
        await using var db = new PharmacyDbContext(Program.DbOptions());
        if (await db.Users.AnyAsync()) throw new InvalidOperationException("An administrator account already exists.");
        db.Users.Add(new PharmacyUser { Username = username.Trim(), PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), Role = PharmacyRole.Admin });
        await db.SaveChangesAsync();
    }

    public async Task<PharmacyUser?> LoginAsync(string username, string password)
    {
        await using var db = new PharmacyDbContext(Program.DbOptions());
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username.Trim() && u.IsActive);
        return user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user : null;
    }
}