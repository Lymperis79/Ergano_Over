namespace ErganiManager.Core.Models;

/// <summary>Greek national/religious holidays shown on the schedule calendar.</summary>
public sealed record GreekHoliday(DateOnly Date, string Name);

public static class GreekHolidays
{
    /// <summary>Returns the commonly observed Greek public holidays for the supplied year.</summary>
    public static IReadOnlyList<GreekHoliday> ForYear(int year)
    {
        var easter = OrthodoxEaster(year);
        return new List<GreekHoliday>
        {
            new(new DateOnly(year, 1, 1), "Πρωτοχρονιά"),
            new(new DateOnly(year, 1, 6), "Θεοφάνεια"),
            new(easter.AddDays(-48), "Καθαρά Δευτέρα"),
            new(new DateOnly(year, 3, 25), "25η Μαρτίου"),
            new(easter.AddDays(-2), "Μεγάλη Παρασκευή"),
            new(easter.AddDays(1), "Δευτέρα του Πάσχα"),
            new(new DateOnly(year, 5, 1), "Πρωτομαγιά"),
            new(new DateOnly(year, 8, 15), "Κοίμηση της Θεοτόκου"),
            new(new DateOnly(year, 10, 28), "28η Οκτωβρίου"),
            new(new DateOnly(year, 12, 25), "Χριστούγεννα"),
            new(new DateOnly(year, 12, 26), "Σύναξη της Θεοτόκου"),
        };
    }

    /// <summary>Computes Orthodox Easter as a Gregorian DateOnly.</summary>
    public static DateOnly OrthodoxEaster(int year)
    {
        // Meeus/Jones/Butcher for the Julian calendar, followed by the
        // Julian-to-Gregorian offset. This covers the years used by ErganiFix.
        int a = year % 4;
        int b = year % 7;
        int c = year % 19;
        int d = (19 * c + 15) % 30;
        int e = (2 * a + 4 * b - d + 34) % 7;
        int month = (d + e + 114) / 31;
        int day = ((d + e + 114) % 31) + 1;
        var julian = new DateOnly(year, month, day);

        // Greece used the Gregorian calendar from 1923; the 13-day offset is
        // valid for the years relevant to the application (1900–2099).
        int offset = year >= 2100 ? 14 : 13;
        return julian.AddDays(offset);
    }
}
