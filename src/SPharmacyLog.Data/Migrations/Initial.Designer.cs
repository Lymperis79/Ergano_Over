using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SPharmacyLog.Data.Migrations;

public sealed partial class Initial : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 64);

        modelBuilder.Entity<PharmacyUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).ValueGeneratedOnAdd();
            entity.Property(user => user.Username).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role).HasConversion<int>();
            entity.Property(user => user.AllowedMenus);
            entity.Property(user => user.IsActive);
            entity.Property(user => user.CanSaveCredentials);
            entity.Property(user => user.CreatedAt);
            entity.HasIndex(user => user.Username).IsUnique();
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.Id).ValueGeneratedOnAdd();
            entity.Property(customer => customer.CustomerNumber).IsRequired();
            entity.Property(customer => customer.FullName).IsRequired();
            entity.Property(customer => customer.Phone).IsRequired();
            entity.Property(customer => customer.Address);
            entity.Property(customer => customer.Notes);
            entity.Property(customer => customer.IsActive);
            entity.Property(customer => customer.CreatedAt);
            entity.HasIndex(customer => customer.CustomerNumber).IsUnique();
        });

        modelBuilder.Entity<Prescription>(entity =>
        {
            entity.ToTable("Prescriptions");
            entity.HasKey(prescription => prescription.Id);
            entity.Property(prescription => prescription.Id).ValueGeneratedOnAdd();
            entity.Property(prescription => prescription.PrescriptionNumber);
            entity.Property(prescription => prescription.MedicineName).IsRequired();
            entity.Property(prescription => prescription.Barcode);
            entity.Property(prescription => prescription.StartDate);
            entity.Property(prescription => prescription.ExpiryDate);
            entity.Property(prescription => prescription.Status).HasConversion<int>();
            entity.Property(prescription => prescription.Notes);
            entity.Property(prescription => prescription.CreatedAt);
            entity.HasIndex(prescription => prescription.ExpiryDate);
            entity.HasOne(prescription => prescription.Customer)
                .WithMany(customer => customer.Prescriptions)
                .HasForeignKey(prescription => prescription.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MedicineCatalogItem>(entity =>
        {
            entity.ToTable("Medicines");
            entity.HasKey(medicine => medicine.Id);
            entity.Property(medicine => medicine.Id).ValueGeneratedOnAdd();
            entity.Property(medicine => medicine.Name).IsRequired();
            entity.Property(medicine => medicine.Barcode);
            entity.Property(medicine => medicine.Notes);
            entity.Property(medicine => medicine.IsActive);
        });

        modelBuilder.Entity<BorrowingRecord>(entity =>
        {
            entity.ToTable("Borrowings");
            entity.HasKey(borrowing => borrowing.Id);
            entity.Property(borrowing => borrowing.Id).ValueGeneratedOnAdd();
            entity.Property(borrowing => borrowing.MedicineName).IsRequired();
            entity.Property(borrowing => borrowing.OriginalBarcode);
            entity.Property(borrowing => borrowing.InternalBarcode).IsRequired();
            entity.Property(borrowing => borrowing.Quantity);
            entity.Property(borrowing => borrowing.BorrowedAt);
            entity.Property(borrowing => borrowing.DueDate);
            entity.Property(borrowing => borrowing.ReturnedAt);
            entity.Property(borrowing => borrowing.Status).HasConversion<int>();
            entity.Property(borrowing => borrowing.Value);
            entity.Property(borrowing => borrowing.Notes);
            entity.HasIndex(borrowing => borrowing.InternalBarcode).IsUnique();
            entity.HasOne(borrowing => borrowing.Customer)
                .WithMany(customer => customer.Borrowings)
                .HasForeignKey(borrowing => borrowing.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("Subscriptions");
            entity.HasKey(subscription => subscription.Id);
            entity.Property(subscription => subscription.Id).ValueGeneratedOnAdd();
            entity.Property(subscription => subscription.Title).IsRequired();
            entity.Property(subscription => subscription.Barcode);
            entity.Property(subscription => subscription.StartDate);
            entity.Property(subscription => subscription.NextDueDate);
            entity.Property(subscription => subscription.ExpiryDate);
            entity.Property(subscription => subscription.Frequency).IsRequired();
            entity.Property(subscription => subscription.Status).HasConversion<int>();
            entity.Property(subscription => subscription.Notes);
            entity.HasOne(subscription => subscription.Customer)
                .WithMany(customer => customer.Subscriptions)
                .HasForeignKey(subscription => subscription.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Id).ValueGeneratedOnAdd();
            entity.Property(payment => payment.Amount);
            entity.Property(payment => payment.PaidAt);
            entity.Property(payment => payment.Method).IsRequired();
            entity.Property(payment => payment.Reference);
            entity.Property(payment => payment.Notes);
            entity.Property(payment => payment.RecordedBy).IsRequired();
            entity.HasOne(payment => payment.Customer)
                .WithMany(customer => customer.Payments)
                .HasForeignKey(payment => payment.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("LedgerEntries");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).ValueGeneratedOnAdd();
            entity.Property(entry => entry.Type).HasConversion<int>();
            entity.Property(entry => entry.Amount);
            entity.Property(entry => entry.EntryDate);
            entity.Property(entry => entry.Description).IsRequired();
            entity.Property(entry => entry.SourceReference);
            entity.Property(entry => entry.RecordedBy).IsRequired();
            entity.HasIndex(entry => new { entry.CustomerId, entry.EntryDate });
            entity.HasOne(entry => entry.Customer)
                .WithMany(customer => customer.LedgerEntries)
                .HasForeignKey(entry => entry.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
