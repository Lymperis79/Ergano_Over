using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data.Entities;

namespace SPharmacyLog.Data;

public sealed class PharmacyDbContext : DbContext
{
    public PharmacyDbContext(DbContextOptions<PharmacyDbContext> options)
        : base(options)
    {
    }

    public DbSet<PharmacyUser> Users => Set<PharmacyUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<MedicineCatalogItem> Medicines => Set<MedicineCatalogItem>();
    public DbSet<BorrowingRecord> Borrowings => Set<BorrowingRecord>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PharmacyUser>()
            .HasIndex(user => user.Username)
            .IsUnique();

        modelBuilder.Entity<Customer>()
            .HasIndex(customer => customer.CustomerNumber)
            .IsUnique();

        modelBuilder.Entity<Prescription>()
            .HasIndex(prescription => prescription.ExpiryDate);

        modelBuilder.Entity<BorrowingRecord>()
            .HasIndex(borrowing => borrowing.InternalBarcode)
            .IsUnique();

        // Keep this index in sync with PharmacyDbContextModelSnapshot and Initial migration.
        modelBuilder.Entity<LedgerEntry>()
            .HasIndex(entry => new { entry.CustomerId, entry.EntryDate });

        modelBuilder.Entity<Prescription>()
            .HasIndex(prescription => prescription.CustomerId);

        modelBuilder.Entity<BorrowingRecord>()
            .HasIndex(borrowing => borrowing.CustomerId);

        modelBuilder.Entity<Subscription>()
            .HasIndex(subscription => subscription.CustomerId);

        modelBuilder.Entity<Payment>()
            .HasIndex(payment => payment.CustomerId);

        modelBuilder.Entity<Prescription>()
            .HasOne(prescription => prescription.Customer)
            .WithMany(customer => customer.Prescriptions)
            .HasForeignKey(prescription => prescription.CustomerId);

        modelBuilder.Entity<BorrowingRecord>()
            .HasOne(borrowing => borrowing.Customer)
            .WithMany(customer => customer.Borrowings)
            .HasForeignKey(borrowing => borrowing.CustomerId);

        modelBuilder.Entity<Subscription>()
            .HasOne(subscription => subscription.Customer)
            .WithMany(customer => customer.Subscriptions)
            .HasForeignKey(subscription => subscription.CustomerId);

        modelBuilder.Entity<Payment>()
            .HasOne(payment => payment.Customer)
            .WithMany(customer => customer.Payments)
            .HasForeignKey(payment => payment.CustomerId);

        modelBuilder.Entity<LedgerEntry>()
            .HasOne(entry => entry.Customer)
            .WithMany(customer => customer.LedgerEntries)
            .HasForeignKey(entry => entry.CustomerId);
    }
}
