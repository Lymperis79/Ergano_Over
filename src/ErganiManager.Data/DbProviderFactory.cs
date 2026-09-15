using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Data;

public static class DbProviderFactory
{
    public static void Configure(DbContextOptionsBuilder builder, DbConfig config)
    {
        switch (config.DatabaseProvider)
        {
            case DatabaseProvider.Sqlite:
                var path = config.SqlitePath
                    ?? throw new InvalidOperationException("SqlitePath must be set for the Sqlite provider.");
                builder.UseSqlite($"Data Source={path}");
                break;

            case DatabaseProvider.SqlServer:
                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                    throw new InvalidOperationException("ConnectionString must be set for the SqlServer provider.");
                builder.UseSqlServer(config.ConnectionString);
                break;

            case DatabaseProvider.MariaDb:
                if (string.IsNullOrWhiteSpace(config.ConnectionString))
                    throw new InvalidOperationException("ConnectionString must be set for the MariaDb provider.");
                builder.UseMySql(
                    config.ConnectionString,
                    ServerVersion.AutoDetect(config.ConnectionString));
                break;

            default:
                throw new NotSupportedException($"Unknown database provider: {config.DatabaseProvider}");
        }
    }

    /// <summary>
    /// Tests whether a connection can be opened with the given config.
    /// Does not create or migrate the schema.
    /// </summary>
    public static async Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(DbConfig config)
    {
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            Configure(optionsBuilder, config);

            await using var context = new AppDbContext(optionsBuilder.Options);
            await context.Database.CanConnectAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
