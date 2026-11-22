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
    public DbSet<MedicineBarcode> MedicineBarcodes => Set<MedicineBarcode>();
    public DbSet<SerializedPackScan> SerializedPackScans => Set<SerializedPackScan>();
    public DbSet<BorrowingRecord> Borrowings => Set<BorrowingRecord>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionAuditEntry> SubscriptionAuditEntries => Set<SubscriptionAuditEntry>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<CalendarNote> CalendarNotes => Set<CalendarNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CalendarNote>()
            .Property(note => note.CalendarDate)
            .HasColumnType("date");

        modelBuilder.Entity<CalendarNote>()
            .Property(note => note.Header)
            .HasMaxLength(200);

        modelBuilder.Entity<CalendarNote>()
            .Property(note => note.CreatedBy)
            .HasMaxLength(255);

        modelBuilder.Entity<CalendarNote>()
            .Property(note => note.IsCompleted)
            .HasDefaultValue(false);

        modelBuilder.Entity<CalendarNote>()
            .Property(note => note.CompletedBy)
            .HasMaxLength(255);

        modelBuilder.Entity<CalendarNote>()
            .HasIndex(note => new { note.CalendarDate, note.IsShared, note.CreatedBy });

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

        modelBuilder.Entity<MedicineBarcode>()
            .Property(barcode => barcode.BarcodeValue)
            .HasMaxLength(64);

        modelBuilder.Entity<MedicineBarcode>()
            .Property(barcode => barcode.BarcodeType)
            .HasMaxLength(24);

        modelBuilder.Entity<MedicineBarcode>()
            .HasIndex(barcode => barcode.BarcodeValue)
            .IsUnique();

        modelBuilder.Entity<MedicineBarcode>()
            .HasIndex(barcode => barcode.MedicineId);

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.RawGs1String)
            .HasMaxLength(255)
            .IsUnicode(false);

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.Gtin14)
            .HasMaxLength(14)
            .IsFixedLength()
            .IsUnicode(false);

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.EofEan13)
            .HasMaxLength(13)
            .IsFixedLength()
            .IsUnicode(false);

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.SerialNumber)
            .HasMaxLength(20)
            .IsUnicode(false);

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.BatchLot)
            .HasMaxLength(20)
            .IsUnicode(false);

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.HmvoStatus)
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasDefaultValue(SerializedPackStatuses.Active);

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.ScannedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<SerializedPackScan>()
            .Property(pack => pack.ExpiryDate)
            .HasColumnType("date");

        modelBuilder.Entity<SerializedPackScan>()
            .HasIndex(pack => new { pack.Gtin14, pack.SerialNumber })
            .IsUnique();

        modelBuilder.Entity<SerializedPackScan>()
            .HasIndex(pack => pack.EofEan13);

        modelBuilder.Entity<SerializedPackScan>()
            .HasIndex(pack => pack.MedicineId);

        modelBuilder.Entity<SerializedPackScan>()
            .HasIndex(pack => new { pack.HmvoStatus, pack.ExpiryDate });

        modelBuilder.Entity<BorrowingRecord>()
            .HasIndex(borrowing => borrowing.SerializedPackScanId);

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

        modelBuilder.Entity<MedicineBarcode>()
            .HasOne(barcode => barcode.Medicine)
            .WithMany(medicine => medicine.Barcodes)
            .HasForeignKey(barcode => barcode.MedicineId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SerializedPackScan>()
            .HasOne(pack => pack.Medicine)
            .WithMany(medicine => medicine.SerializedPacks)
            .HasForeignKey(pack => pack.MedicineId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<BorrowingRecord>()
            .HasOne(borrowing => borrowing.SerializedPackScan)
            .WithMany(pack => pack.Borrowings)
            .HasForeignKey(borrowing => borrowing.SerializedPackScanId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Subscription>()
            .HasOne(subscription => subscription.Customer)
            .WithMany(customer => customer.Subscriptions)
            .HasForeignKey(subscription => subscription.CustomerId);

        modelBuilder.Entity<Subscription>()
            .HasIndex(subscription => subscription.Status);

        modelBuilder.Entity<SubscriptionAuditEntry>()
            .Property(entry => entry.Action)
            .HasMaxLength(255);

        modelBuilder.Entity<SubscriptionAuditEntry>()
            .Property(entry => entry.ChangedBy)
            .HasMaxLength(255);

        modelBuilder.Entity<Subscription>()
            .Property(subscription => subscription.CreatedBy)
            .HasMaxLength(255);

        modelBuilder.Entity<Subscription>()
            .Property(subscription => subscription.LastModifiedBy)
            .HasMaxLength(255);

        modelBuilder.Entity<SubscriptionAuditEntry>()
            .HasIndex(entry => new { entry.SubscriptionId, entry.ChangedAt });

        modelBuilder.Entity<SubscriptionAuditEntry>()
            .HasOne(entry => entry.Subscription)
            .WithMany(subscription => subscription.AuditEntries)
            .HasForeignKey(entry => entry.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

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
