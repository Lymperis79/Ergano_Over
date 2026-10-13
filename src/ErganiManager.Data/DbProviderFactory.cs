using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Text.RegularExpressions;

namespace ErganiManager.Data;

public static class DbProviderFactory
{
    public static void Configure(DbContextOptionsBuilder builder, DbConfig config)
    {
        switch (config.DatabaseProvider)
        {
            case DatabaseProvider.Sqlite:
                var path = config.SqlitePath
                    ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("SqlitePath must be set for the Sqlite provider."));
                builder.UseSqlite($"Data Source={path}");
                break;

            case DatabaseProvider.SqlServer:
                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                    throw new InvalidOperationException(ErganiManager.Data.UiText.T("ConnectionString must be set for the SqlServer provider."));
                builder.UseSqlServer(config.ConnectionString);
                break;

            case DatabaseProvider.MariaDb:
                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                    throw new InvalidOperationException(ErganiManager.Data.UiText.T("ConnectionString must be set for the MariaDb provider."));
                builder.UseMySql(
                    config.ConnectionString,
                    ServerVersion.AutoDetect(config.ConnectionString));
                break;

            default:
                throw new NotSupportedException($"Unknown database provider: {config.DatabaseProvider}");
        }
    }

    /// <summary>
    /// Creates the configured server-side database when the server is reachable but
    /// the application database does not exist yet. For SQLite there is nothing to
    /// create here; EF creates the file when the schema is applied.
    /// </summary>
    public static async Task EnsureDatabaseExistsAsync(DbConfig config)
    {
        switch (config.DatabaseProvider)
        {
            case DatabaseProvider.Sqlite:
            {
                var sqlitePath = config.SqlitePath
                    ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("SqlitePath must be set for the Sqlite provider."));
                var dir = Path.GetDirectoryName(sqlitePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                // Opening a connection creates the (empty) database file.
                await using var sqliteConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sqlitePath}");
                await sqliteConnection.OpenAsync();
                return;
            }

            case DatabaseProvider.SqlServer:
            {
                var cs = config.ConnectionString
                    ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("ConnectionString must be set for the SqlServer provider."));
                var builder = new SqlConnectionStringBuilder(cs);
                var database = builder.InitialCatalog;
                if (string.IsNullOrWhiteSpace(database))
                    throw new InvalidOperationException(ErganiManager.Data.UiText.T("SQL Server database name is required."));
                ValidateDatabaseName(database);

                var serverBuilder = new SqlConnectionStringBuilder(cs)
                {
                    InitialCatalog = "master"
                };
                await using var connection = new SqlConnection(serverBuilder.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"IF DB_ID(N'{database.Replace("'", "''")}') IS NULL CREATE DATABASE [{database.Replace("]", "]]" )}]";
                await command.ExecuteNonQueryAsync();
                return;
            }

            case DatabaseProvider.MariaDb:
            {
                var cs = config.ConnectionString
                    ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("ConnectionString must be set for the MariaDB provider."));
                var builder = new MySqlConnectionStringBuilder(cs);
                var database = builder.Database;
                if (string.IsNullOrWhiteSpace(database))
                    throw new InvalidOperationException(ErganiManager.Data.UiText.T("MariaDB database name is required."));
                ValidateDatabaseName(database);

                var serverBuilder = new MySqlConnectionStringBuilder(cs)
                {
                    Database = string.Empty
                };
                await using var connection = new MySqlConnection(serverBuilder.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{database.Replace("`", "``")}`";
                await command.ExecuteNonQueryAsync();
                return;
            }

            default:
                throw new NotSupportedException($"Unknown database provider: {config.DatabaseProvider}");
        }
    }

    private static void ValidateDatabaseName(string name)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_.$-]+$"))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("The database name contains unsupported characters."));
    }

    /// <summary>
    /// Tests whether the configured DATABASE exists and a connection can be opened.
    /// Does not create or migrate the schema.
    /// (CanConnectAsync returns false instead of throwing when the server is down or
    /// the database does not exist, so its result must be checked.)
    /// </summary>
    public static async Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(DbConfig config)
    {
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            Configure(optionsBuilder, config);

            await using var context = new AppDbContext(optionsBuilder.Options);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (await context.Database.CanConnectAsync(cts.Token))
                return (true, null);

            return (false, "The database server is not reachable or the database does not exist.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Tests whether the database SERVER is reachable, without requiring the application
    /// database to exist yet (used by the setup wizard before the database is created).
    /// </summary>
    public static async Task<(bool Success, string? ErrorMessage)> TestServerAsync(DbConfig config)
    {
        try
        {
            switch (config.DatabaseProvider)
            {
                case DatabaseProvider.Sqlite:
                {
                    var path = config.SqlitePath
                        ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("SqlitePath must be set for the Sqlite provider."));
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    return (true, null);
                }

                case DatabaseProvider.SqlServer:
                {
                    var cs = new SqlConnectionStringBuilder(config.ConnectionString) { InitialCatalog = "master" };
                    await using var c = new SqlConnection(cs.ConnectionString);
                    await c.OpenAsync();
                    return (true, null);
                }

                case DatabaseProvider.MariaDb:
                {
                    var cs = new MySqlConnectionStringBuilder(config.ConnectionString) { Database = string.Empty };
                    await using var c = new MySqlConnection(cs.ConnectionString);
                    await c.OpenAsync();
                    return (true, null);
                }

                default:
                    return (false, $"Unknown database provider: {config.DatabaseProvider}");
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
