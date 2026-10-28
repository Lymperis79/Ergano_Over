namespace ErganiManager.Core.Models;

/// <summary>A holiday catalogue record returned to the UI.</summary>
public sealed record OfficialHolidayRecord(DateOnly Date, string Name, bool IsCustom, string Source);
