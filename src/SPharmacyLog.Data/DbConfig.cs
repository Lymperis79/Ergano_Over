namespace SPharmacyLog.Data;

public enum DatabaseProvider { Sqlite, SqlServer, MariaDb }

/// <summary>Local connection settings, stored outside the database.</summary>
public sealed class DbConfig
{
    public DatabaseProvider DatabaseProvider { get; set; } = DatabaseProvider.Sqlite;
    public string? SqlitePath { get; set; }
    public string? ConnectionString { get; set; }

    public static DbConfig DefaultSqlite(string appDataFolder) => new()
    {
        DatabaseProvider = DatabaseProvider.Sqlite,
        SqlitePath = Path.Combine(appDataFolder, "pharmacy.db")
    };
}
