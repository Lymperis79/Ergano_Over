using System.Text.Json;

namespace ErganiManager.Core.Models;

/// <summary>Helpers for the list of official holiday dates enabled as non-working days by a company.</summary>
public static class CompanyHolidayCalendar
{
    public static HashSet<string> Parse(string? json)
    {
        try
        {
            return (JsonSerializer.Deserialize<HashSet<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json)
                    ?? new HashSet<string>())
                .Where(value => DateOnly.TryParse(value, out _))
                .Select(value => DateOnly.Parse(value).ToString("yyyy-MM-dd"))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public static bool IsEnabled(string? json, DateOnly date) =>
        Parse(json).Contains(date.ToString("yyyy-MM-dd"));

    public static string Serialize(IEnumerable<DateOnly> dates) =>
        JsonSerializer.Serialize(dates.Distinct().OrderBy(date => date)
            .Select(date => date.ToString("yyyy-MM-dd")).ToArray());
}
