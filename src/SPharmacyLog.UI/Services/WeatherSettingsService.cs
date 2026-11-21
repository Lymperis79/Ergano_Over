using System.Net.Http;
using System.Text.Json;

namespace SPharmacyLog.UI.Services;

public sealed class WeatherSettings
{
    public string Location { get; set; } = "";
    public bool ShowLocation { get; set; } = true;
    public bool ShowTemperature { get; set; } = true;
    public bool ShowCondition { get; set; } = true;
    public bool ShowFeelsLike { get; set; } = false;
    public bool ShowHumidity { get; set; } = true;
    public bool ShowWind { get; set; } = false;
}

public sealed record WeatherSnapshot(double Temperature, double FeelsLike, int Humidity, double WindSpeed, int WeatherCode);

public static class WeatherSettingsService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static readonly string FilePath = Path.Combine(Folder, "weather.json");

    public static WeatherSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WeatherSettings>(File.ReadAllText(FilePath)) ?? new WeatherSettings();
        }
        catch { }
        return new WeatherSettings();
    }

    public static void Save(WeatherSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static async Task<WeatherSnapshot> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(location))
            throw new InvalidOperationException("Configure a weather location in Administrative > Weather.");

        double latitude, longitude;
        var parts = location.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out latitude)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out longitude))
        {
            // Coordinates were supplied directly.
        }
        else
        {
            var geoUrl = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(location) + "&count=1&language=en&format=json";
            using var geoResponse = await Http.GetAsync(geoUrl, cancellationToken);
            geoResponse.EnsureSuccessStatusCode();
            using var geo = JsonDocument.Parse(await geoResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                throw new InvalidOperationException("Weather location was not found. Enter a city or coordinates as latitude, longitude.");
            latitude = results[0].GetProperty("latitude").GetDouble();
            longitude = results[0].GetProperty("longitude").GetDouble();
        }

        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var current = json.RootElement.GetProperty("current");
        return new WeatherSnapshot(
            current.GetProperty("temperature_2m").GetDouble(),
            current.GetProperty("apparent_temperature").GetDouble(),
            current.GetProperty("relative_humidity_2m").GetInt32(),
            current.GetProperty("wind_speed_10m").GetDouble(),
            current.GetProperty("weather_code").GetInt32());
    }

    public static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 => "Mainly clear / partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Weather unavailable"
    };
}
