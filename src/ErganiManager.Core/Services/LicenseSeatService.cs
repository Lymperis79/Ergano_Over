using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.Licensing;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public enum SeatStatus
{
    /// <summary>This PC may run.</summary>
    Granted,
    /// <summary>All licensed PCs are in use by other PCs.</summary>
    Denied,
    /// <summary>The shared database cannot be reached, so the number of PCs cannot be checked now.</summary>
    DatabaseUnavailable
}

public sealed record SeatResult(SeatStatus Status, int Limit, IReadOnlyList<string> InUseBy);

/// <summary>
/// Limits how many PCs may run the program at the same time for licenses with a seat limit (a "single user" license
/// that follows the user from PC to PC has one seat, a "multi-user" license has several). The PCs that use the same
/// database register themselves in it; the PCs that started first keep their seat.
/// </summary>
public sealed class LicenseSeatService
{
    /// <summary>A PC that did not report for this long is considered closed.</summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(3);

    private readonly IConnectionStateService _connectionState;

    public LicenseSeatService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    /// <summary>Takes (or refreshes) the seat of this PC. Called at startup and then about once a minute.</summary>
    public async Task<SeatResult> AcquireAsync(LicensePayload license)
    {
        var limit = Math.Max(1, license.Seats);
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var machine = MachineCode.Normalize(MachineCode.Get());
            var now = DateTime.UtcNow;
            var cutoff = now - ActiveWindow;

            await db.LicenseSessions.Where(s => s.LastSeenUtc < cutoff).ExecuteDeleteAsync();

            var mine = await db.LicenseSessions.FirstOrDefaultAsync(s => s.MachineCode == machine);
            if (mine == null)
            {
                db.LicenseSessions.Add(new LicenseSession
                {
                    MachineCode = machine,
                    MachineName = Environment.MachineName,
                    Licensee = license.Licensee,
                    StartedUtc = now,
                    LastSeenUtc = now
                });
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // A second copy of the program on this PC registered at the same moment: refresh that row.
                    db.ChangeTracker.Clear();
                    mine = await db.LicenseSessions.FirstOrDefaultAsync(s => s.MachineCode == machine);
                    if (mine != null)
                    {
                        mine.LastSeenUtc = now;
                        await db.SaveChangesAsync();
                    }
                }
            }
            else
            {
                mine.LastSeenUtc = now;
                await db.SaveChangesAsync();
            }

            // The PCs that started first keep their seats; if two PCs register at the same moment the order is
            // still the same for both, so exactly one of them gets the last seat.
            var active = await db.LicenseSessions.AsNoTracking()
                .Where(s => s.LastSeenUtc >= cutoff)
                .OrderBy(s => s.StartedUtc).ThenBy(s => s.MachineCode)
                .ToListAsync();

            var withSeat = active.Take(limit).ToList();
            if (withSeat.Any(s => s.MachineCode == machine))
                return new SeatResult(SeatStatus.Granted, limit, withSeat.Select(s => s.MachineName).ToList());

            await db.LicenseSessions.Where(s => s.MachineCode == machine).ExecuteDeleteAsync();   // give it back
            return new SeatResult(SeatStatus.Denied, limit, withSeat.Select(s => s.MachineName).ToList());
        }
        catch (Exception)
        {
            return new SeatResult(SeatStatus.DatabaseUnavailable, limit, Array.Empty<string>());
        }
    }

    /// <summary>Frees the seat of this PC when the program closes. Never throws.</summary>
    public async Task ReleaseAsync()
    {
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var machine = MachineCode.Normalize(MachineCode.Get());
            await db.LicenseSessions.Where(s => s.MachineCode == machine).ExecuteDeleteAsync();
        }
        catch (Exception)
        {
            // Not critical: the seat is free again after a few minutes.
        }
    }
}
