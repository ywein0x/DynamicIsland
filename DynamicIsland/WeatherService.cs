using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

public sealed record HomeWeather(string City, int Now, string Sky, int Max, int Min);

public sealed record ForecastDay(DateTime Date, int Max, int Min, string Sky, int RainPercent);

/// <summary>Weather from Open-Meteo: free, no key needed.</summary>
static class WeatherService
{
    static readonly HttpClient Http = CreateClient();

    public static async Task<HomeWeather?> CurrentAsync(string city)
    {
        try
        {
            if (await GeocodeAsync(city) is not { } place)
                return null;
            using var forecast = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://api.open-meteo.com/v1/forecast?latitude={place.Lat}&longitude={place.Lon}" +
                "&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1"));
            var root = forecast.RootElement;
            var current = root.GetProperty("current");
            var daily = root.GetProperty("daily");
            return new HomeWeather(place.Name,
                (int)Math.Round(current.GetProperty("temperature_2m").GetDouble()),
                Describe(current.GetProperty("weather_code").GetInt32()),
                (int)Math.Round(daily.GetProperty("temperature_2m_max")[0].GetDouble()),
                (int)Math.Round(daily.GetProperty("temperature_2m_min")[0].GetDouble()));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<List<ForecastDay>?> ForecastAsync(string city, int days)
    {
        try
        {
            if (await GeocodeAsync(city) is not { } place)
                return null;
            using var forecast = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://api.open-meteo.com/v1/forecast?latitude={place.Lat}&longitude={place.Lon}" +
                "&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max" +
                $"&timezone=auto&forecast_days={days}"));
            var daily = forecast.RootElement.GetProperty("daily");
            return Enumerable.Range(0, days).Select(d => new ForecastDay(
                DateTime.Today.AddDays(d),
                (int)Math.Round(daily.GetProperty("temperature_2m_max")[d].GetDouble()),
                (int)Math.Round(daily.GetProperty("temperature_2m_min")[d].GetDouble()),
                Describe(daily.GetProperty("weather_code")[d].GetInt32()),
                daily.GetProperty("precipitation_probability_max")[d] is { ValueKind: JsonValueKind.Number } rain ? rain.GetInt32() : 0)).ToList();
        }
        catch
        {
            return null;
        }
    }

    static async Task<(string Name, string Lat, string Lon)?> GeocodeAsync(string name)
    {
        using var geo = JsonDocument.Parse(await Http.GetStringAsync(
            $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=1&language=tr"));
        if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;
        var place = results[0];
        return (place.GetProperty("name").GetString() ?? name,
            place.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture),
            place.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>WMO weather codes in Turkish.</summary>
    static string Describe(int code) => code switch
    {
        0 => "açık",
        1 => "çoğunlukla açık",
        2 => "parçalı bulutlu",
        3 => "kapalı",
        45 or 48 => "sisli",
        >= 51 and <= 57 => "çiseliyor",
        >= 61 and <= 67 => "yağmurlu",
        >= 71 and <= 77 => "karlı",
        >= 80 and <= 82 => "sağanak yağışlı",
        85 or 86 => "kar sağanaklı",
        >= 95 => "gök gürültülü fırtınalı",
        _ => "değişken",
    };

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland (https://github.com/ywein0x/DynamicIsland)");
        return http;
    }
}
