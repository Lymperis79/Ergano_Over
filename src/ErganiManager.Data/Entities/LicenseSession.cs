using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// A PC that is running the program right now under a license that limits how many PCs may run at the same time.
/// Every running program refreshes its row about once a minute; rows that were not refreshed for a few minutes
/// (a PC that was switched off or crashed) no longer count.
/// </summary>
public class LicenseSession
{
    [Key, MaxLength(40)]
    public string MachineCode { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MachineName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Licensee { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }
}
