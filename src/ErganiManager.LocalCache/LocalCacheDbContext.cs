using ErganiManager.LocalCache.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.LocalCache;

public class LocalCacheDbContext : DbContext
{
    public LocalCacheDbContext(DbContextOptions<LocalCacheDbContext> options) : base(options) { }

    public DbSet<CachedUser> CachedUsers => Set<CachedUser>();
    public DbSet<CachedCompanySettings> CachedCompanySettings => Set<CachedCompanySettings>();
    public DbSet<CachedEmployee> CachedEmployees => Set<CachedEmployee>();
    public DbSet<CachedSchedule> CachedSchedules => Set<CachedSchedule>();
    public DbSet<PendingSubmission> PendingSubmissions => Set<PendingSubmission>();
    public DbSet<FailedSubmission> FailedSubmissions => Set<FailedSubmission>();
    public DbSet<LocalSyncChange> LocalSyncChanges => Set<LocalSyncChange>();
    public DbSet<LocalSyncCheckpoint> LocalSyncCheckpoints => Set<LocalSyncCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CachedUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<CachedCompanySettings>(e =>
        {
            e.HasKey(s => s.CompanyId);
        });

        modelBuilder.Entity<CachedEmployee>(e =>
        {
            e.HasIndex(emp => new { emp.CompanyId, emp.BarcodeId }).IsUnique();
        });

        modelBuilder.Entity<CachedSchedule>(e =>
        {
            e.HasIndex(s => new { s.EmployeeId, s.ScheduleDate });
        });

        modelBuilder.Entity<PendingSubmission>(e =>
        {
            e.HasIndex(p => p.Synced);
            e.HasIndex(p => p.ClientEventId).IsUnique();
        });

        modelBuilder.Entity<FailedSubmission>(e =>
        {
            e.HasIndex(f => f.Resolved);
            e.HasIndex(f => f.CompanyId);
        });

        modelBuilder.Entity<LocalSyncChange>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ChangeId).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.CompanyId, x.EntityType, x.EntityKey });
            e.Property(x => x.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<LocalSyncCheckpoint>(e =>
        {
            e.HasKey(x => x.Key);
            e.HasIndex(x => new { x.CompanyId, x.DeviceId });
        });
    }
}
