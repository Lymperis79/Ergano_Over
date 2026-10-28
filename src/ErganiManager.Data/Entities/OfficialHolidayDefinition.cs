namespace ErganiManager.Data.Entities;

/// <summary>
/// Permanent shared catalogue of public and custom holidays. Records are retained for historical reference.
/// </summary>
public sealed class OfficialHolidayDefinition
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "GR";
    public string Source { get; set; } = "Nager.Date";
    public bool IsCustom { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAtUtc { get; set; }
}
