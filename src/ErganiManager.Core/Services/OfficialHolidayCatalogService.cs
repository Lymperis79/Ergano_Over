using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

/// <summary>
/// Synchronizes Greek public holidays from Nager.Date into the shared main database.
/// The database is permanent: synchronization only inserts or updates records and never deletes rows.
/// </summary>
public sealed class OfficialHolidayCatalogService : IOfficialHolidayCatalogService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://date.nager.at/api/v3/"),
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly IConnectionStateService _connection;

    public OfficialHolidayCatalogService(IConnectionStateService connection) => _connection = connection;

    private AppDbContext OpenDb() => new(_connection.GetDbOptions());

    public async Task<IReadOnlyList<OfficialHolidayRecord>> GetForYearsAsync(IEnumerable<int> years)
    {
        var requestedYears = years.Distinct().OrderBy(y => y).ToArray();
        await using var db = OpenDb();
        return await db.OfficialHolidayDefinitions
            .Where(h => requestedYears.Contains(h.Date.Year))
            .OrderBy(h => h.Date)
            .Select(h => new OfficialHolidayRecord(h.Date, h.Name, h.IsCustom, h.Source))
            .ToListAsync();
    }

    public async Task<int> SynchronizeAsync(IEnumerable<int> years)
    {
        var synchronized = 0;
        var uniqueYears = years.Where(y => y is >= 1900 and <= 2200).Distinct().OrderBy(y => y);

        foreach (var year in uniqueYears)
        {
            try
            {
                var holidays = await Http.GetFromJsonAsync<List<NagerHoliday>>($"PublicHolidays/{year}/GR");
                if (holidays == null || holidays.Count == 0)
                    throw new InvalidOperationException($"The holiday feed returned no records for {year}.");

                await using var db = OpenDb();
                var existing = await db.OfficialHolidayDefinitions
                    .Where(h => h.Date >= new DateOnly(year, 1, 1) && h.Date <= new DateOnly(year, 12, 31))
                    .ToListAsync();
                var byDate = existing.ToDictionary(h => h.Date);

                foreach (var item in holidays)
                {
                    if (!DateOnly.TryParse(item.Date, out var date)) continue;
                    var name = string.IsNullOrWhiteSpace(item.LocalName) ? item.Name : item.LocalName.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    if (byDate.TryGetValue(date, out var row))
                    {
                        // Custom records belong to the administrator; never overwrite their name.
                        if (!row.IsCustom)
                        {
                            row.Name = name;
                            row.Source = "Nager.Date";
                            row.CountryCode = "GR";
                            row.LastSyncedAtUtc = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        var newRow = new OfficialHolidayDefinition
                        {
                            Date = date,
                            Name = name,
                            CountryCode = "GR",
                            Source = "Nager.Date",
                            IsCustom = false,
                            CreatedAtUtc = DateTime.UtcNow,
                            LastSyncedAtUtc = DateTime.UtcNow
                        };
                        db.OfficialHolidayDefinitions.Add(newRow);
                        byDate[date] = newRow;
                    }
                }

                await db.SaveChangesAsync();
                synchronized++;
            }
            catch
            {
                // Preserve all existing rows and continue to other years if offline or the feed is unavailable.
                // Only when a year has no catalogue rows at all, seed the built-in Greek calendar as a one-time fallback.
                try
                {
                    await using var db = OpenDb();
                    var hasRows = await db.OfficialHolidayDefinitions.AnyAsync(h => h.Date.Year == year);
                    if (!hasRows)
                    {
                        foreach (var holiday in GreekHolidays.ForYear(year))
                        {
                            db.OfficialHolidayDefinitions.Add(new OfficialHolidayDefinition
                            {
                                Date = holiday.Date,
                                Name = holiday.Name,
                                CountryCode = "GR",
                                Source = "Built-in fallback",
                                IsCustom = false,
                                CreatedAtUtc = DateTime.UtcNow
                            });
                        }
                        await db.SaveChangesAsync();
                    }
                }
                catch
                {
                    // The main database may itself be unavailable; no local file is used as the live catalogue.
                }
            }
        }

        return synchronized;
    }

    public async Task AddCustomAsync(DateOnly date, string name)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new InvalidOperationException("A holiday name is required.");

        await using var db = OpenDb();
        if (await db.OfficialHolidayDefinitions.AnyAsync(h => h.Date == date))
            throw new InvalidOperationException($"A holiday is already registered for {date:dd/MM/yyyy}.");

        db.OfficialHolidayDefinitions.Add(new OfficialHolidayDefinition
        {
            Date = date,
            Name = trimmedName,
            CountryCode = "GR",
            Source = "Administrator",
            IsCustom = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed class NagerHoliday
    {
        [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
        [JsonPropertyName("localName")] public string LocalName { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }
}
