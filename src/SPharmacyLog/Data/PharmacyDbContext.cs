using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Models;

namespace SPharmacyLog.Data;

public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options) : DbContext(options)
{
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
        modelBuilder.Entity<PharmacyUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(x => x.CustomerNumber).IsUnique();
        modelBuilder.Entity<Prescription>().HasIndex(x => x.ExpiryDate);
        modelBuilder.Entity<Prescription>().HasIndex(x => x.Barcode);
        modelBuilder.Entity<BorrowingRecord>().HasIndex(x => x.InternalBarcode).IsUnique();
        modelBuilder.Entity<Subscription>().HasIndex(x => x.Barcode).IsUnique();
        modelBuilder.Entity<LedgerEntry>().HasIndex(x => new { x.CustomerId, x.EntryDate });
        modelBuilder.Entity<Prescription>().HasOne(x => x.Customer).WithMany(x => x.Prescriptions).HasForeignKey(x => x.CustomerId);
        modelBuilder.Entity<BorrowingRecord>().HasOne(x => x.Customer).WithMany(x => x.Borrowings).HasForeignKey(x => x.CustomerId);
        modelBuilder.Entity<Subscription>().HasOne(x => x.Customer).WithMany(x => x.Subscriptions).HasForeignKey(x => x.CustomerId);
        modelBuilder.Entity<Payment>().HasOne(x => x.Customer).WithMany(x => x.Payments).HasForeignKey(x => x.CustomerId);
        modelBuilder.Entity<LedgerEntry>().HasOne(x => x.Customer).WithMany(x => x.LedgerEntries).HasForeignKey(x => x.CustomerId);
    }
}