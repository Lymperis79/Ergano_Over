namespace ErganiManager.Data;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
    MariaDb
}

/// <summary>
/// Connection configuration. This is loaded from a local JSON file
/// (NOT stored in the database itself, since the database may not exist yet).
/// </summary>
public class DbConfig
{
    public DatabaseProvider DatabaseProvider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>
    /// For SQLite: the file path. For SqlServer/MariaDb: leave null, use ConnectionString.
    /// </summary>
    public string? SqlitePath { get; set; }

    /// <summary>
    /// Full ADO.NET connection string for SqlServer or MariaDb.
    /// </summary>
    public string? ConnectionString { get; set; }

    public DateTime? LastTestedSuccessfully { get; set; }

    public static DbConfig DefaultSqlite(string appDataFolder)
    {
        return new DbConfig
        {
            DatabaseProvider = DatabaseProvider.Sqlite,
            SqlitePath = Path.Combine(appDataFolder, "erganimanager.db")
        };
    }
}
