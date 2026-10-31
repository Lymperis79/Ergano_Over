using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<BusinessBranch> Branches => Set<BusinessBranch>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeSchedule> Schedules => Set<EmployeeSchedule>();
    public DbSet<ScheduleChangeHistory> ScheduleChangeHistory => Set<ScheduleChangeHistory>();
    public DbSet<ScheduleWorkingPeriod> ScheduleWorkingPeriods => Set<ScheduleWorkingPeriod>();
    public DbSet<WorkCard> WorkCards => Set<WorkCard>();
    public DbSet<Overtime> Overtimes => Set<Overtime>();
    public DbSet<Leave> Leaves => Set<Leave>();
    public DbSet<LeaveTypeConfiguration> LeaveTypeConfigurations => Set<LeaveTypeConfiguration>();
    public DbSet<ApiSubmissionLog> ApiSubmissionLogs => Set<ApiSubmissionLog>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<AppSecret> AppSecrets => Set<AppSecret>();
    public DbSet<LicenseSession> LicenseSessions => Set<LicenseSession>();
    public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
    public DbSet<EmployeeImport> EmployeeImports => Set<EmployeeImport>();
    public DbSet<ErganiImportLog> ErganiImportLogs => Set<ErganiImportLog>();
    public DbSet<ScheduleCloneBatch> ScheduleCloneBatches => Set<ScheduleCloneBatch>();
    public DbSet<OfficialHolidayDefinition> OfficialHolidayDefinitions => Set<OfficialHolidayDefinition>();
    public DbSet<TerminalAnnouncement> TerminalAnnouncements => Set<TerminalAnnouncement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Terminal announcements ─────────────────────────────
        modelBuilder.Entity<TerminalAnnouncement>(e =>
        {
            e.Property(a => a.Message).HasMaxLength(200).IsRequired();
            e.HasIndex(a => new { a.CompanyId, a.IsActive, a.ExpiresAtUtc });
        });

        // ── Permanent official holiday catalogue ───────────────
        modelBuilder.Entity<OfficialHolidayDefinition>(e =>
        {
            e.ToTable("OfficialHolidayDefinitions");
            e.HasIndex(h => h.Date).IsUnique();
            e.Property(h => h.Name).HasMaxLength(200).IsRequired();
            e.Property(h => h.CountryCode).HasMaxLength(2).IsRequired();
            e.Property(h => h.Source).HasMaxLength(40).IsRequired();
        });

        // ── Company ──────────────────────────────────────────
        modelBuilder.Entity<Company>(e =>
        {
            e.HasIndex(c => c.TaxId);

            // Existing companies keep the old behaviour (Rest/Absent days blocked) when the column is
            // added to an existing database. ValueGeneratedNever makes EF always write the value, so an
            // unticked box (false) is saved as false instead of being replaced by the default.
            e.Property(c => c.BlockClockInOnRestOrAbsentDay)
                .HasDefaultValue(true)
                .ValueGeneratedNever();

            e.Property(c => c.OfficialHolidayDatesJson)
                .HasDefaultValue("[]")
                .ValueGeneratedNever();
        });

        // ── BusinessBranch ───────────────────────────────────
        modelBuilder.Entity<BusinessBranch>(e =>
        {
            e.HasOne(b => b.Company)
                .WithMany(c => c.Branches)
                .HasForeignKey(b => b.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(b => new { b.CompanyId, b.BranchNumber }).IsUnique();
        });

        // ── AppUser ───────────────────────────────────────────
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();

            // NoAction on both FKs to avoid SQL Server's "multiple cascade paths"
            // error (NU1605 equivalent at the DB level). AppUser.CompanyId and
            // AppUser.BranchId are both nullable — nulling them on company/branch
            // deletion is handled in the service layer (CompanyService/BranchService)
            // rather than relying on a DB-level cascade.
            e.HasOne(u => u.Company)
                .WithMany(c => c.Users)
                .HasForeignKey(u => u.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(u => u.Branch)
                .WithMany()
                .HasForeignKey(u => u.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            // Stored as text. "Limited" (7 letters) still fits columns created when the longest role was
            // "Operator" (8 letters); older/narrow values are read tolerantly.
            e.Property(u => u.Role)
                .HasConversion(v => v.ToString(), s => UserRoleText.Parse(s))
                .HasMaxLength(20);
        });

        // ── Employee ──────────────────────────────────────────
        modelBuilder.Entity<Employee>(e =>
        {
            e.HasOne(emp => emp.Company)
                .WithMany(c => c.Employees)
                .HasForeignKey(emp => emp.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(emp => emp.Branch)
                .WithMany(b => b.Employees)
                .HasForeignKey(emp => emp.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            // Barcode IDs must be unique within a company, not globally
            e.HasIndex(emp => new { emp.CompanyId, emp.BarcodeId }).IsUnique();
            e.HasIndex(emp => emp.TaxId);
            e.Property(emp => emp.NormalDailyHours)
                .HasDefaultValue(8d)
                .ValueGeneratedNever();
        });

        // ── EmployeeSchedule ──────────────────────────────────
        modelBuilder.Entity<EmployeeSchedule>(e =>
        {
            e.HasOne(s => s.Employee)
                .WithMany(emp => emp.Schedules)
                .HasForeignKey(s => s.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.Branch)
                .WithMany()
                .HasForeignKey(s => s.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(s => new { s.EmployeeId, s.ScheduleDate });

            e.Property(s => s.WorkType).HasConversion<string>();
            e.Property(s => s.ScheduleType).HasConversion<string>();
            e.Property(s => s.Source).HasConversion<string>();
        });

        modelBuilder.Entity<ScheduleChangeHistory>(e =>
        {
            e.HasIndex(h => new { h.EmployeeId, h.ScheduleDate, h.ChangedAtUtc });
            e.Property(h => h.ChangeSource).HasMaxLength(30);
            e.Property(h => h.NewWorkType).HasMaxLength(30);
        });

        // ── Schedule working periods (split shifts) ────────────────────────
        modelBuilder.Entity<ScheduleWorkingPeriod>(e =>
        {
            e.HasOne(p => p.Schedule)
                .WithMany(s => s.WorkingPeriods)
                .HasForeignKey(p => p.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(p => new { p.ScheduleId, p.PeriodNumber }).IsUnique();
        });

        // ── Clone history (snapshot used by "Undo last clone") ─
        modelBuilder.Entity<ScheduleCloneBatch>(e =>
        {
            e.HasIndex(b => b.CompanyId);
        });

        // ── WorkCard ──────────────────────────────────────────
        modelBuilder.Entity<WorkCard>(e =>
        {
            e.HasOne(w => w.Employee)
                .WithMany(emp => emp.WorkCards)
                .HasForeignKey(w => w.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(w => w.Branch)
                .WithMany()
                .HasForeignKey(w => w.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(w => new { w.EmployeeId, w.MovementDateTime });

            e.Property(w => w.MovementType).HasConversion<string>();
        });

        // ── Overtime ──────────────────────────────────────────
        modelBuilder.Entity<Overtime>(e =>
        {
            e.HasOne(o => o.Employee)
                .WithMany(emp => emp.Overtimes)
                .HasForeignKey(o => o.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(o => o.Branch)
                .WithMany()
                .HasForeignKey(o => o.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            e.Property(o => o.Justification).HasConversion<string>();
        });

        // ── Leave (holidays) ──────────────────────────────────
        modelBuilder.Entity<Leave>(e =>
        {
            e.HasOne(l => l.Employee)
                .WithMany()
                .HasForeignKey(l => l.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Branch)
                .WithMany()
                .HasForeignKey(l => l.BranchId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(l => new { l.EmployeeId, l.LeaveDate });
        });

        // ── Logging ───────────────────────────────────────────
        modelBuilder.Entity<ApiSubmissionLog>(e =>
        {
            e.HasIndex(l => l.SubmissionDate);
            e.HasIndex(l => l.CompanyId);
        });

        modelBuilder.Entity<EmailLog>(e =>
        {
            e.HasIndex(l => new { l.CompanyId, l.CreatedAt });
        });

        modelBuilder.Entity<AppLog>(e =>
        {
            e.HasIndex(l => l.Timestamp);
        });

        // ── Reporting ─────────────────────────────────────────
        modelBuilder.Entity<ReportDefinition>(e =>
        {
            e.HasIndex(r => r.CompanyId);
        });

        modelBuilder.Entity<EmployeeImport>(e =>
        {
            e.HasIndex(i => i.CompanyId);
        });
    }
}
