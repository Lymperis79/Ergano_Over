using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SPharmacyLog.Data;

/// <summary>Central database configuration used by every application DbContext.</summary>
public static class PharmacyDb
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string AppFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");

    public static string ConfigurationPath => Path.Combine(AppFolder, "connection.json");
    public static string DefaultSqlitePath => Path.Combine(AppFolder, "pharmacy.db");

    public static DbConfig? LoadConfig()
    {
        if (!File.Exists(ConfigurationPath)) return null;
        try { return JsonSerializer.Deserialize<DbConfig>(File.ReadAllText(ConfigurationPath), JsonOptions); }
        catch { return null; }
    }

    public static void SaveConfig(DbConfig config)
    {
        Directory.CreateDirectory(AppFolder);
        var temp = ConfigurationPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temp, ConfigurationPath, overwrite: true);
    }

    public static DbContextOptions<PharmacyDbContext> Options() =>
        CreateOptions(LoadConfig() ?? throw new InvalidOperationException(
            "The database has not been configured. Complete database setup first."));

    public static DbContextOptions<PharmacyDbContext> CreateOptions(DbConfig config)
    {
        var builder = new DbContextOptionsBuilder<PharmacyDbContext>();
        DbProviderFactory.Configure(builder, config);
        return builder.Options;
    }

    public static async Task ApplyMigrationsAsync(DbConfig config)
    {
        await using var db = new PharmacyDbContext(CreateOptions(config));
        await db.Database.MigrateAsync();
    }

    public static async Task<bool> HasUsersAsync(DbConfig config)
    {
        await using var db = new PharmacyDbContext(CreateOptions(config));
        return await db.Users.AnyAsync();
    }
}
