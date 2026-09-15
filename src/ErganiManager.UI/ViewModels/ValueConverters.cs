using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ErganiManager.Core.Interfaces;

namespace ErganiManager.UI.ViewModels;

/// <summary>Converts MovementType string ("Arrival"/"Departure") to a colour dot icon.</summary>
public class MovementTypeIconConverter : IValueConverter
{
    public static readonly MovementTypeIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == "Arrival" ? "🟢" : "🔴";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts a bool to a background brush — green for true, grey for false.</summary>
public class BoolToBackgroundConverter : IValueConverter
{
    public static readonly BoolToBackgroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.Parse("#1B5E20"))   // dark green
            : new SolidColorBrush(Color.Parse("#37474F"));  // dark grey

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts bool SubmittedToErgani to a status label.</summary>
public class SubmittedTextConverter : IValueConverter
{
    public static readonly SubmittedTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "✅ Submitted" : "⏳ Pending";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts bool IsCancelled to a status label.</summary>
public class CancelledTextConverter : IValueConverter
{
    public static readonly CancelledTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "❌ Cancelled" : "✅ Active";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts AppOvertimeJustification enum value to its human-readable label.</summary>
public class OvertimeJustificationConverter : IValueConverter
{
    public static readonly OvertimeJustificationConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AppOvertimeJustification j) return string.Empty;
        return j switch
        {
            AppOvertimeJustification.AccidentPreventionOrDamageRestoration => "Accident prevention / damage restoration",
            AppOvertimeJustification.UrgentSeasonalTasks                   => "Urgent seasonal tasks",
            AppOvertimeJustification.ExceptionalWorkload                   => "Exceptional workload",
            AppOvertimeJustification.SupplementaryTasks                    => "Supplementary tasks",
            AppOvertimeJustification.LostHoursSuddenCauses                 => "Lost hours — sudden causes",
            AppOvertimeJustification.LostHoursOfficialHolidays             => "Lost hours — official holidays",
            AppOvertimeJustification.LostHoursWeatherConditions            => "Lost hours — weather conditions",
            AppOvertimeJustification.EmergencyClosureDay                   => "Emergency closure day",
            AppOvertimeJustification.NonWorkdayTasks                       => "Non-workday tasks",
            _                                                              => j.ToString()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts AppLanguage enum to its display name.</summary>
public class AppLanguageConverter : IValueConverter
{
    public static readonly AppLanguageConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AppLanguage lang ? lang switch
        {
            AppLanguage.Greek   => "Ελληνικά",
            _                   => "English"
        } : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
