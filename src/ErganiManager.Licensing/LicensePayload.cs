using System.Globalization;
using System.Text.Json.Serialization;

namespace ErganiManager.Licensing;

/// <summary>What a license says. It is signed as a whole, so none of it can be edited.</summary>
public sealed class LicensePayload
{
    [JsonPropertyName("v")]   public int Version { get; set; } = 1;
    [JsonPropertyName("lic")] public string Licensee { get; set; } = string.Empty;

    /// <summary>The PC code the license is for, or "*" for a license that is not tied to one PC.</summary>
    [JsonPropertyName("pc")]  public string MachineCode { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd</summary>
    [JsonPropertyName("iss")] public string Issued { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd of the last valid day; null = permanent.</summary>
    [JsonPropertyName("exp")] public string? Expires { get; set; }

    /// <summary>
    /// How many PCs may run the program at the same time, for a license that is not tied to one PC:
    /// 1 = single user (one PC at a time, any PC), 2 or more = multi-user, 0 = no limit.
    /// Ignored for a license that is locked to one PC.
    /// </summary>
    [JsonPropertyName("seats")] public int Seats { get; set; }

    /// <summary>The modules (see <see cref="LicenseModules"/>); "*" = all.</summary>
    [JsonPropertyName("mod")] public List<string> Modules { get; set; } = new() { LicenseModules.All };

    [JsonIgnore]
    public DateOnly? ExpiresDate =>
        DateOnly.TryParseExact(Expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [JsonIgnore]
    public bool IsPermanent => string.IsNullOrWhiteSpace(Expires);

    /// <summary>True for a license that is locked to one PC (its PC code).</summary>
    [JsonIgnore]
    public bool IsMachineBound => MachineCode != "*";

    /// <summary>True when the number of PCs running at the same time is limited (checked in the shared database).</summary>
    [JsonIgnore]
    public bool HasSeatLimit => !IsMachineBound && Seats > 0;

    /// <summary>Days left including today (null = permanent; negative = expired).</summary>
    public int? DaysLeft(DateOnly today) => ExpiresDate is { } e ? e.DayNumber - today.DayNumber + 1 : null;
}
