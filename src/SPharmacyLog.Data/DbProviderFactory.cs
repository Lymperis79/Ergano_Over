using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;
using System.Text.RegularExpressions;

namespace SPharmacyLog.Data;

public static class DbProviderFactory
{
    public static void Configure(DbContextOptionsBuilder builder, DbConfig config)
    {
        // This application deliberately shares one provider-neutral migration history
        // across SQLite, SQL Server and MariaDB. Provider-specific relational annotations
        // (for example identifier limits/type mappings) can otherwise make EF Core 10
        // report PendingModelChangesWarning even when the migration set is intentional.
        // Schema changes must still be introduced as reviewed migrations.
        builder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));

        switch (config.DatabaseProvider)
        {
            case DatabaseProvider.Sqlite:
                if (string.IsNullOrWhiteSpace(config.SqlitePath))
                    throw new InvalidOperationException("A SQLite database path is required.");
                var directory = Path.GetDirectoryName(config.SqlitePath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                builder.UseSqlite($"Data Source={config.SqlitePath};Foreign Keys=True");
                break;
            case DatabaseProvider.SqlServer:
                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                    throw new InvalidOperationException("A SQL Server connection string is required.");
                builder.UseSqlServer(config.ConnectionString);
                break;
            case DatabaseProvider.MariaDb:
                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                    throw new InvalidOperationException("A MariaDB connection string is required.");
                builder.UseMySql(config.ConnectionString, ServerVersion.AutoDetect(config.ConnectionString));
                break;
            default:
                throw new NotSupportedException($"Unsupported database provider: {config.DatabaseProvider}");
        }
    }

    public static async Task<(bool Success, string? Error)> TestConnectionAsync(DbConfig config)
    {
        try
        {
            var options = new DbContextOptionsBuilder<PharmacyDbContext>();
            Configure(options, config);
            await using var db = new PharmacyDbContext(options.Options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await db.Database.CanConnectAsync(timeout.Token)
                ? (true, null) : (false, "The database is not reachable or does not exist.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static async Task<(bool Success, string? Error)> TestServerAsync(DbConfig config)
    {
        try
        {
            switch (config.DatabaseProvider)
            {
                case DatabaseProvider.Sqlite:
                    if (string.IsNullOrWhiteSpace(config.SqlitePath))
                        return (false, "A SQLite database path is required.");
                    var folder = Path.GetDirectoryName(config.SqlitePath);
                    if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
                    return (true, null);
                case DatabaseProvider.SqlServer:
                    var sql = new SqlConnectionStringBuilder(config.ConnectionString) { InitialCatalog = "master" };
                    await using (var connection = new SqlConnection(sql.ConnectionString)) await connection.OpenAsync();
                    return (true, null);
                case DatabaseProvider.MariaDb:
                    var maria = new MySqlConnectionStringBuilder(config.ConnectionString) { Database = string.Empty };
                    await using (var connection = new MySqlConnection(maria.ConnectionString)) await connection.OpenAsync();
                    return (true, null);
                default: return (false, "Unsupported database provider.");
            }
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static async Task EnsureDatabaseExistsAsync(DbConfig config)
    {
        switch (config.DatabaseProvider)
        {
            case DatabaseProvider.Sqlite:
                if (string.IsNullOrWhiteSpace(config.SqlitePath))
                    throw new InvalidOperationException("A SQLite database path is required.");
                var folder = Path.GetDirectoryName(config.SqlitePath);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
                await using (var sqlite = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={config.SqlitePath}"))
                    await sqlite.OpenAsync();
                return;
            case DatabaseProvider.SqlServer:
                var sql = new SqlConnectionStringBuilder(config.ConnectionString);
                var sqlName = sql.InitialCatalog;
                ValidateDatabaseName(sqlName);
                sql.InitialCatalog = "master";
                await using (var connection = new SqlConnection(sql.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"IF DB_ID(N'{sqlName.Replace("'", "''")}') IS NULL CREATE DATABASE [{sqlName.Replace("]", "]]")}]";
                    await command.ExecuteNonQueryAsync();
                }
                return;
            case DatabaseProvider.MariaDb:
                var maria = new MySqlConnectionStringBuilder(config.ConnectionString);
                var mariaName = maria.Database;
                ValidateDatabaseName(mariaName);
                maria.Database = string.Empty;
                await using (var connection = new MySqlConnection(maria.ConnectionString))
                {
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{mariaName.Replace("`", "``")}`";
                    await command.ExecuteNonQueryAsync();
                }
                return;
            default: throw new NotSupportedException($"Unsupported database provider: {config.DatabaseProvider}");
        }
    }

    private static void ValidateDatabaseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"^[A-Za-z0-9_.$-]+$"))
            throw new InvalidOperationException("The database name is missing or contains unsupported characters.");
    }
}
