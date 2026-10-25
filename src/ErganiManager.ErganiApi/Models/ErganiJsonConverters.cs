using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

/// <summary>DateOnly -> "dd/MM/yyyy" (Ergani overtime format).</summary>
public sealed class ErganiDateConverter : JsonConverter<DateOnly>
{
    private const string Format = "dd/MM/yyyy";
    public override DateOnly Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => DateOnly.ParseExact(reader.GetString()!, Format, CultureInfo.InvariantCulture);
    public override void Write(Utf8JsonWriter w, DateOnly v, JsonSerializerOptions o)
        => w.WriteStringValue(v.ToString(Format, CultureInfo.InvariantCulture));
}

/// <summary>TimeOnly -> "HH:mm".</summary>
public sealed class ErganiTimeConverter : JsonConverter<TimeOnly>
{
    private const string Format = "HH:mm";
    public override TimeOnly Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => TimeOnly.ParseExact(reader.GetString()!, Format, CultureInfo.InvariantCulture);
    public override void Write(Utf8JsonWriter w, TimeOnly v, JsonSerializerOptions o)
        => w.WriteStringValue(v.ToString(Format, CultureInfo.InvariantCulture));
}

/// <summary>bool -> "0" / "1".</summary>
public sealed class ErganiBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => reader.TokenType == JsonTokenType.String ? reader.GetString() == "1" : reader.GetBoolean();
    public override void Write(Utf8JsonWriter w, bool v, JsonSerializerOptions o)
        => w.WriteStringValue(v ? "1" : "0");
}

/// <summary>int -> JSON string ("5", "0").</summary>
public sealed class ErganiIntAsStringConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => reader.TokenType == JsonTokenType.String
            ? int.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
            : reader.GetInt32();
    public override void Write(Utf8JsonWriter w, int v, JsonSerializerOptions o)
        => w.WriteStringValue(v.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Overtime justification enum -> Ergani f_reason code ("001".."009").</summary>
public sealed class ErganiOvertimeReasonConverter : JsonConverter<ApiOvertimeJustification>
{
    public static string ToCode(ApiOvertimeJustification v) => v switch
    {
        ApiOvertimeJustification.ACCIDENT_PREVENTION_OR_DAMAGE_RESTORATION => "001",
        ApiOvertimeJustification.URGENT_SEASONAL_TASKS                     => "002",
        ApiOvertimeJustification.EXCEPTIONAL_WORKLOAD                      => "003",
        ApiOvertimeJustification.SUPPLEMENTARY_TASKS                       => "004",
        ApiOvertimeJustification.LOST_HOURS_SUDDEN_CAUSES                  => "005",
        ApiOvertimeJustification.LOST_HOURS_OFFICIAL_HOLIDAYS              => "006",
        ApiOvertimeJustification.LOST_HOURS_WEATHER_CONDITIONS             => "007",
        ApiOvertimeJustification.EMERGENCY_CLOSURE_DAY                     => "008",
        ApiOvertimeJustification.NON_WORKDAY_TASKS                         => "009",
        _                                                                  => "003"
    };

    public override ApiOvertimeJustification Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var code = reader.GetString();
        foreach (var v in Enum.GetValues<ApiOvertimeJustification>())
            if (ToCode(v) == code) return v;
        return ApiOvertimeJustification.EXCEPTIONAL_WORKLOAD;
    }

    public override void Write(Utf8JsonWriter w, ApiOvertimeJustification v, JsonSerializerOptions o)
        => w.WriteStringValue(ToCode(v));
}
