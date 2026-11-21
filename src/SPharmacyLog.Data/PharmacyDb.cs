using Microsoft.EntityFrameworkCore;

namespace SPharmacyLog.Data;

public static class PharmacyDb
{
    public static DbContextOptions<PharmacyDbContext> Options()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SPharmacyLog");

        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "pharmacy.db");

        return new DbContextOptionsBuilder<PharmacyDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
    }
}
