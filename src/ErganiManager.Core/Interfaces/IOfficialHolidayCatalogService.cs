using ErganiManager.Core.Models;

namespace ErganiManager.Core.Interfaces;

public interface IOfficialHolidayCatalogService
{
    /// <summary>Synchronizes the requested years when online, then returns their permanent database records.</summary>
    Task<IReadOnlyList<OfficialHolidayRecord>> GetForYearsAsync(IEnumerable<int> years);

    /// <summary>Manually synchronizes official Greek holidays from the external feed. Existing rows are never deleted.</summary>
    Task<int> SynchronizeAsync(IEnumerable<int> years);

    /// <summary>Adds a custom holiday to the permanent catalogue without replacing an existing date.</summary>
    Task AddCustomAsync(DateOnly date, string name);
}
