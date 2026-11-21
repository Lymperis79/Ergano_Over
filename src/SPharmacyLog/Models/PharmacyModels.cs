namespace SPharmacyLog.Models;

public enum PharmacyRole { Admin = 0, Operator = 1, Limited = 2 }
public enum PrescriptionStatus { Active = 0, Expired = 1, Completed = 2, Cancelled = 3 }
public enum BorrowingStatus { Borrowed = 0, Returned = 1, Cancelled = 2 }
public enum SubscriptionStatus { Active = 0, Paused = 1, Completed = 2, Cancelled = 3 }
public enum LedgerEntryType { Charge = 0, Payment = 1, Adjustment = 2, Reversal = 3 }

public class PharmacyUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public PharmacyRole Role { get; set; } = PharmacyRole.Operator;
    public string? AllowedMenus { get; set; }
    public bool IsActive { get; set; } = true;
    public bool CanSaveCredentials { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Customer
{
    public int Id { get; set; }
    public string CustomerNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public ICollection<BorrowingRecord> Borrowings { get; set; } = new List<BorrowingRecord>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
}

public class Prescription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string? PrescriptionNumber { get; set; }
    public string MedicineName { get; set; } = "";
    public string? Barcode { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime ExpiryDate { get; set; }
    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Active;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MedicineCatalogItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Barcode { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BorrowingRecord
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string MedicineName { get; set; } = "";
    public string? OriginalBarcode { get; set; }
    public string InternalBarcode { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public DateTime BorrowedAt { get; set; } = DateTime.Now;
    public DateTime? DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public BorrowingStatus Status { get; set; } = BorrowingStatus.Borrowed;
    public decimal? Value { get; set; }
    public string? Notes { get; set; }
}

public class Subscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Title { get; set; } = "";
    public string? Barcode { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime NextDueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string Frequency { get; set; } = "Monthly";
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public string? Notes { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.Now;
    public string Method { get; set; } = "Cash";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = "";
}

public class LedgerEntry
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public LedgerEntryType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
    public string? SourceReference { get; set; }
    public string RecordedBy { get; set; } = "";
}