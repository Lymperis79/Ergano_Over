namespace SPharmacyLog.Data.Entities;

/// <summary>
/// Defines the access level assigned to a pharmacy application user.
/// </summary>
public enum PharmacyRole
{
    Admin,
    Operator,
    Limited
}

public enum PrescriptionStatus
{
    Active,
    Expired,
    Completed,
    Cancelled
}

public enum BorrowingStatus
{
    Borrowed,
    Returned,
    Cancelled
}

public enum SubscriptionStatus
{
    Active,
    Paused,
    Completed,
    Cancelled
}

public enum LedgerEntryType
{
    Charge,
    Payment,
    Adjustment,
    Reversal
}

/// <summary>
/// A user account that can sign in to S Pharmacy Log.
/// </summary>
public class PharmacyUser
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public PharmacyRole Role { get; set; }

    /// <summary>
    /// Optional serialized list of menu permissions.
    /// </summary>
    public string? AllowedMenus { get; set; }

    public bool IsActive { get; set; } = true;

    public bool CanSaveCredentials { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A customer registered in the pharmacy.
/// </summary>
public class Customer
{
    public int Id { get; set; }

    public string CustomerNumber { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string? Address { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Prescription> Prescriptions { get; set; } =
        new List<Prescription>();

    public ICollection<BorrowingRecord> Borrowings { get; set; } =
        new List<BorrowingRecord>();

    public ICollection<Subscription> Subscriptions { get; set; } =
        new List<Subscription>();

    public ICollection<Payment> Payments { get; set; } =
        new List<Payment>();

    public ICollection<LedgerEntry> LedgerEntries { get; set; } =
        new List<LedgerEntry>();
}

/// <summary>
/// A prescription and its expiry information.
/// </summary>
public class Prescription
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string? PrescriptionNumber { get; set; }

    public string MedicineName { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;

    public DateTime ExpiryDate { get; set; }

    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Active;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A medicine available in the pharmacy catalogue.
/// </summary>
public class MedicineCatalogItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A medicine or item borrowed by a customer.
/// </summary>
public class BorrowingRecord
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string MedicineName { get; set; } = string.Empty;

    public string? OriginalBarcode { get; set; }

    public string InternalBarcode { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    public DateTime BorrowedAt { get; set; } = DateTime.Now;

    public DateTime? DueDate { get; set; }

    public DateTime? ReturnedAt { get; set; }

    public BorrowingStatus Status { get; set; } = BorrowingStatus.Borrowed;

    public decimal? Value { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// A customer subscription with its next due date.
/// </summary>
public class Subscription
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;

    public DateTime NextDueDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public string Frequency { get; set; } = "Monthly";

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public string? Notes { get; set; }
}

/// <summary>
/// A payment received from a customer.
/// </summary>
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

    public string RecordedBy { get; set; } = string.Empty;
}

/// <summary>
/// A debit, credit, or adjustment in a customer's account ledger.
/// </summary>
public class LedgerEntry
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public LedgerEntryType Type { get; set; }

    public decimal Amount { get; set; }

    public DateTime EntryDate { get; set; } = DateTime.Now;

    public string Description { get; set; } = string.Empty;

    public string? SourceReference { get; set; }

    public string RecordedBy { get; set; } = string.Empty;
}
