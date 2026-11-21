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
    Active = 0,
    Paused = 1, // Retained for compatibility with existing records.
    Executed = 2,
    Cancelled = 3 // Retained for compatibility with existing records.
}

public enum LedgerEntryType
{
    Charge,
    Payment,
    Adjustment,
    Reversal
}

/// <summary>
/// Lifecycle state of a serialized pharmaceutical pack.
/// Values are stored as text to match GS1/HMVO terminology.
/// </summary>
public static class SerializedPackStatuses
{
    public const string Active = "ACTIVE";
    public const string Dispensed = "DISPENSED";
    public const string Returned = "RETURNED";
    public const string Alert = "ALERT";
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

    public string? FirstName { get; set; }

    public string? Surname { get; set; }

    public string? Telephone { get; set; }

    public string? Mobile { get; set; }

    public string? Email { get; set; }

    public string? Amka { get; set; }

    public string? Insurance { get; set; }

    public string? City { get; set; }

    public string? Postcode { get; set; }

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

    /// <summary>All known manufacturer, EOF/NTIN and other barcodes for this medicine.</summary>
    public ICollection<MedicineBarcode> Barcodes { get; set; } = new List<MedicineBarcode>();

    /// <summary>Serialized physical packs identified by GS1 DataMatrix.</summary>
    public ICollection<SerializedPackScan> SerializedPacks { get; set; } = new List<SerializedPackScan>();
}

/// <summary>
/// An alternate barcode that resolves to a medicine catalogue item.
/// A medicine may have multiple barcode rows (for example GTIN and EOF/NTIN).
/// </summary>
public class MedicineBarcode
{
    public int Id { get; set; }

    public int MedicineId { get; set; }

    public MedicineCatalogItem? Medicine { get; set; }

    /// <summary>Exact barcode value without scanner symbology prefixes.</summary>
    public string BarcodeValue { get; set; } = string.Empty;

    /// <summary>GTIN14, EOF_EAN13, OTHER or LEGACY.</summary>
    public string BarcodeType { get; set; } = "OTHER";
}

/// <summary>
/// One physical medicine pack scanned from a GS1 DataMatrix.
/// The raw payload is retained exactly, including ASCII 29 group separators.
/// </summary>
public class SerializedPackScan
{
    public int Id { get; set; }

    /// <summary>Exact scanner output; ASCII 29 (0x1D) must be preserved.</summary>
    public string RawGs1String { get; set; } = string.Empty;

    /// <summary>GS1 AI (01), exactly 14 digits.</summary>
    public string Gtin14 { get; set; } = string.Empty;

    /// <summary>Optional mapped Greek EOF/NTIN barcode, exactly 13 digits.</summary>
    public string? EofEan13 { get; set; }

    /// <summary>GS1 AI (21).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>GS1 AI (17), interpreted as a calendar date.</summary>
    public DateTime ExpiryDate { get; set; }

    /// <summary>GS1 AI (10).</summary>
    public string BatchLot { get; set; } = string.Empty;

    /// <summary>ACTIVE, DISPENSED, RETURNED or ALERT.</summary>
    public string HmvoStatus { get; set; } = SerializedPackStatuses.Active;

    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Resolved medicine catalogue item; null when not yet matched.</summary>
    public int? MedicineId { get; set; }

    public MedicineCatalogItem? Medicine { get; set; }

    public ICollection<BorrowingRecord> Borrowings { get; set; } = new List<BorrowingRecord>();
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

    /// <summary>Optional link to the exact serialized pack scanned for this borrowing.</summary>
    public int? SerializedPackScanId { get; set; }

    public SerializedPackScan? SerializedPackScan { get; set; }

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedBy { get; set; } = "system";

    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    public string LastModifiedBy { get; set; } = "system";

    public ICollection<SubscriptionAuditEntry> AuditEntries { get; set; } = new List<SubscriptionAuditEntry>();
}

/// <summary>
/// Immutable audit event for a subscription creation, edit or status transition.
/// </summary>
public class SubscriptionAuditEntry
{
    public int Id { get; set; }

    public int SubscriptionId { get; set; }

    public Subscription? Subscription { get; set; }

    public string Action { get; set; } = string.Empty;

    public int? PreviousStatus { get; set; }

    public int NewStatus { get; set; }

    public string ChangedBy { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public string? Details { get; set; }
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
