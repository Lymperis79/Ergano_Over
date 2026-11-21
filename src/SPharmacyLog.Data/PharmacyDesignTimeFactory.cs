using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SPharmacyLog.Data;

/// <summary>
/// Creates a PharmacyDbContext for Entity Framework Core design-time commands,
/// such as adding or inspecting migrations.
/// </summary>
public sealed class PharmacyDesignTimeFactory
    : IDesignTimeDbContextFactory<PharmacyDbContext>
{
    public PharmacyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PharmacyDbContext>()
            .UseSqlite("Data Source=pharmacy-design.db")
            .Options;

        return new PharmacyDbContext(options);
    }
}
