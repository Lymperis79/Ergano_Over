using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ErganiManager.Data;

/// <summary>
/// Used by `dotnet ef migrations add` at design time.
/// Defaults to SQLite so migrations tooling works without any external DB running.
/// Run with: dotnet ef migrations add InitialCreate -- --provider Sqlite
/// (See README in this project for per-provider migration instructions.)
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = ParseProviderArg(args);

        var config = provider switch
        {
            DatabaseProvider.SqlServer => new DbConfig
            {
                DatabaseProvider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=localhost\\SQLEXPRESS;Database=ErganiManager;Trusted_Connection=True;TrustServerCertificate=True;"
            },
            DatabaseProvider.MariaDb => new DbConfig
            {
                DatabaseProvider = DatabaseProvider.MariaDb,
                ConnectionString = "Server=localhost;Database=erganimanager;User=root;Password=changeme;"
            },
            _ => new DbConfig
            {
                DatabaseProvider = DatabaseProvider.Sqlite,
                SqlitePath = "design_time.db"
            }
        };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);

        return new AppDbContext(optionsBuilder.Options);
    }

    private static DatabaseProvider ParseProviderArg(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--provider" && Enum.TryParse<DatabaseProvider>(args[i + 1], true, out var parsed))
                return parsed;
        }
        return DatabaseProvider.Sqlite;
    }
}
