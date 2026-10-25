using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GlobalWeatherTracker;

public class Program
{
    private static readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly (string City, string Country, double Lat, double Lon)[] GlobalHubs =
    [
        ("London", "United Kingdom", 51.5074, -0.1278),
        ("Tokyo", "Japan", 35.6762, 139.6503),
        ("New York", "United States", 40.7128, -74.0060),
        ("Sydney", "Australia", -33.8688, 151.2093),
        ("Cairo", "Egypt", 30.0444, 31.2357),
        ("São Paulo", "Brazil", -23.5505, -46.6333),
        ("Reykjavik", "Iceland", 64.1466, -21.9426),
        ("Nairobi", "Kenya", -1.2921, 36.8219),
        ("Singapore", "Singapore", 1.3521, 103.8198),
        ("McMurdo Station", "Antarctica", -77.8460, 166.6681)
    ];

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        while (true)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================");
            Console.WriteLine("               GLOBAL WEATHER TRACKER                     ");
            Console.WriteLine("==========================================================");
            Console.ResetColor();
            Console.WriteLine("1. Global Weather Snapshot (Across all continents)");
            Console.WriteLine("2. Look Up Any City Worldwide");
            Console.WriteLine("3. Look Up by Exact Coordinates (Latitude, Longitude)");
            Console.WriteLine("4. Exit");
            Console.WriteLine();
            Console.Write("Select an option (1-4): ");

            string? choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    await ScanGlobalHubsAsync();
                    break;
                case "2":
                    await SearchByCityAsync();
                    break;
                case "3":
                    await SearchByCoordinatesAsync();
                    break;
                case "4":
                    Console.WriteLine("\nExiting. Stay safe in any weather!");
                    return;
                default:
                    Console.WriteLine("\nInvalid option. Press any key to continue...");
                    Console.ReadKey();
                    continue;
            }

            Console.WriteLine("\nPress any key to return to the main menu...");
            Console.ReadKey();
        }
    }

    private static async Task ScanGlobalHubsAsync()
    {
        Console.WriteLine("\nFetching live atmospheric reports across the globe...\n");
        Console.WriteLine("{0,-20} {1,-18} {2,8} {3,12} {4,-20}", "City", "Country", "Temp", "Wind", "Condition");
        Console.WriteLine(new string('-', 82));

        var tasks = new List<Task<(string City, string Country, WeatherResult? Data)>>();

        foreach (var hub in GlobalHubs)
        {
            tasks.Add(Task.Run(async () =>
            {
                var weather = await FetchWeatherAsync(hub.Lat, hub.Lon);
                return (hub.City, hub.Country, weather);
            }));
        }

        var results = await Task.WhenAll(tasks);

        foreach (var report in results)
        {
            if (report.Data != null)
            {
                string condition = InterpretWeatherCode(report.Data.Current.WeatherCode);
                Console.WriteLine(
                    "{0,-20} {1,-18} {2,6:F1}°C {3,8:F1} km/h  {4,-20}",
                    report.City,
                    report.Country,
                    report.Data.Current.Temperature,
                    report.Data.Current.WindSpeed,
                    condition
                );
            }
            else
            {
                Console.WriteLine("{0,-20} {1,-18} [Error reading telemetry]", report.City, report.Country);
            }
        }
    }

    private static async Task SearchByCityAsync()
    {
        Console.Write("\nEnter city name: ");
        string? query = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine("City name cannot be empty.");
            return;
        }

        Console.WriteLine($"Geocoding '{query}'...");
        var geoResult = await GeocodeCityAsync(query);

        if (geoResult?.Results == null || geoResult.Results.Count == 0)
        {
            Console.WriteLine($"Location '{query}' could not be resolved on Earth.");
            return;
        }

        var location = geoResult.Results[0];
        Console.WriteLine($"Found: {location.Name}, {location.Country} (Lat: {location.Latitude:F4}, Lon: {location.Longitude:F4})");
        Console.WriteLine("Retrieving live telemetry...");

        var weather = await FetchWeatherAsync(location.Latitude, location.Longitude);
        DisplaySingleReport($"{location.Name}, {location.Country}", weather);
    }

    private static async Task SearchByCoordinatesAsync()
    {
        Console.Write("\nEnter Latitude (-90 to 90): ");
        if (!double.TryParse(Console.ReadLine(), out double lat) || lat < -90 || lat > 90)
        {
            Console.WriteLine("Invalid latitude value.");
            return;
        }

        Console.Write("Enter Longitude (-180 to 180): ");
        if (!double.TryParse(Console.ReadLine(), out double lon) || lon < -180 || lon > 180)
        {
            Console.WriteLine("Invalid longitude value.");
            return;
        }

        Console.WriteLine($"Retrieving weather for ({lat:F4}, {lon:F4})...");
        var weather = await FetchWeatherAsync(lat, lon);
        DisplaySingleReport($"Coordinates ({lat:F4}, {lon:F4})", weather);
    }

    private static void DisplaySingleReport(string title, WeatherResult? report)
    {
        if (report == null)
        {
            Console.WriteLine("Failed to fetch weather data for the specified location.");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n--- LIVE WEATHER REPORT: {title.ToUpper()} ---");
        Console.ResetColor();
        Console.WriteLine($"Condition:         {InterpretWeatherCode(report.Current.WeatherCode)}");
        Console.WriteLine($"Temperature:       {report.Current.Temperature:F1} °C ({CelsiusToFahrenheit(report.Current.Temperature):F1} °F)");
        Console.WriteLine($"Relative Humidity: {report.Current.RelativeHumidity} %");
        Console.WriteLine($"Wind Velocity:     {report.Current.WindSpeed:F1} km/h (Direction: {report.Current.WindDirection}°)");
        Console.WriteLine($"Atmospheric Day:   {(report.Current.IsDay == 1 ? "Daylight" : "Night")}");
        Console.WriteLine($"Telemetry Time:    {report.Current.Time} (UTC)");
    }

    private static async Task<WeatherResult?> FetchWeatherAsync(double latitude, double longitude)
    {
        string url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,relative_humidity_2m,is_day,weather_code,wind_speed_10m,wind_direction_10m";

        try
        {
            return await httpClient.GetFromJsonAsync<WeatherResult>(url);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GeocodingResponse?> GeocodeCityAsync(string cityName)
    {
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityName)}&count=1&language=en&format=json";

        try
        {
            return await httpClient.GetFromJsonAsync<GeocodingResponse>(url);
        }
        catch
        {
            return null;
        }
    }

    private static double CelsiusToFahrenheit(double c) => (c * 9 / 5) + 32;

    private static string InterpretWeatherCode(int code) => code switch
    {
        0 => "Clear Sky ☀️",
        1 => "Mainly Clear 🌤️",
        2 => "Partly Cloudy ⛅",
        3 => "Overcast ☁️",
        45 or 48 => "Fog / Depositing Rime Fog 🌫️",
        51 or 53 or 55 => "Drizzle 🌦️",
        61 or 63 or 65 => "Rain 🌧️",
        71 or 73 or 75 => "Snowfall 🌨️",
        77 => "Snow Grains ❄️",
        80 or 81 or 82 => "Rain Showers 🌧️",
        85 or 86 => "Snow Showers 🌨️",
        95 => "Thunderstorm ⛈️",
        96 or 99 => "Thunderstorm with Hail ⛈️⚡",
        _ => $"Code {code} (Unknown)"
    };
}

public class WeatherResult
{
    [JsonPropertyName("current")]
    public CurrentWeather Current { get; set; } = new();
}

public class CurrentWeather
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("temperature_2m")]
    public double Temperature { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public int RelativeHumidity { get; set; }

    [JsonPropertyName("is_day")]
    public int IsDay { get; set; }

    [JsonPropertyName("weather_code")]
    public int WeatherCode { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public double WindSpeed { get; set; }

    [JsonPropertyName("wind_direction_10m")]
    public int WindDirection { get; set; }
}

public class GeocodingResponse
{
    [JsonPropertyName("results")]
    public List<GeocodingLocation>? Results { get; set; }
}

public class GeocodingLocation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}