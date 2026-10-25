using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GlobalWeatherTracker;

public class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public class MainForm : Form
{
    private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly TextBox cityInput = new();
    private readonly Button searchButton = new();
    private readonly Label statusLabel = new();
    private readonly Label weatherInfoLabel = new();
    private readonly ListBox hubsListBox = new();
    private readonly WebBrowser mapBrowser = new();

    private static readonly (string City, string Country, double Lat, double Lon)[] GlobalHubs =
    [
        ("London", "United Kingdom", 51.5074, -0.1278),
        ("Hanoi", "Vietnam", 21.0285, 105.8542),
        ("Ho Chi Minh City", "Vietnam", 10.8231, 106.6297),
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

    public MainForm()
    {
        Text = "Global Weather Tracker & Interactive World Atlas";
        Width = 1100;
        Height = 740;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        // Top Search Bar
        Panel topPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 65,
            BackColor = Color.FromArgb(240, 243, 246),
            Padding = new Padding(15, 15, 15, 10)
        };

        Label searchLabel = new() { Text = "Search City:", Left = 15, Top = 20, Width = 85, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        cityInput = new() { Left = 105, Top = 18, Width = 260, PlaceholderText = "e.g. Hanoi, London, Tokyo..." };
        searchButton = new() { Text = "Get Weather & Map", Left = 375, Top = 16, Width = 160, Height = 30 };
        searchButton.Click += async (s, e) => await SearchCityWeatherAsync();
        AcceptButton = searchButton;

        statusLabel = new() { Left = 550, Top = 22, Width = 500, ForeColor = Color.DarkSlateGray };

        topPanel.Controls.AddRange(new Control[] { searchLabel, cityInput, searchButton, statusLabel });

        // Left Panel (Telemetry & Quick Global Hubs)
        Panel leftPanel = new()
        {
            Dock = DockStyle.Left,
            Width = 340,
            BackColor = Color.FromArgb(249, 250, 251),
            Padding = new Padding(15)
        };

        Label telemetryHeader = new()
        {
            Text = "Live Weather & Local Time",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 30
        };

        weatherInfoLabel = new()
        {
            Text = "Search for a city or double-click a hub below to view live telemetry, local time, and map coordinates.",
            Dock = DockStyle.Top,
            Height = 240,
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(33, 37, 41)
        };

        Label hubsHeader = new()
        {
            Text = "Quick Global Hubs (Double-click):",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 25
        };

        hubsListBox = new()
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9)
        };
        foreach (var hub in GlobalHubs)
        {
            hubsListBox.Items.Add($"{hub.City}, {hub.Country}");
        }
        hubsListBox.DoubleClick += async (s, e) => await SelectHubAsync();

        leftPanel.Controls.Add(hubsListBox);
        leftPanel.Controls.Add(hubsHeader);
        leftPanel.Controls.Add(weatherInfoLabel);
        leftPanel.Controls.Add(telemetryHeader);

        // Center / Right Panel (Interactive World Map)
        mapBrowser = new()
        {
            Dock = DockStyle.Fill,
            ScriptErrorsSuppressed = true
        };

        Controls.Add(mapBrowser);
        Controls.Add(leftPanel);
        Controls.Add(topPanel);

        // Load initial default view (Hanoi)
        Load += async (s, e) => await FetchAndDisplayLocationAsync("Hanoi", "Vietnam", 21.0285, 105.8542);
    }

    private async Task SearchCityWeatherAsync()
    {
        string city = cityInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(city))
        {
            MessageBox.Show("Please enter a city name to search.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        statusLabel.Text = $"Geocoding '{city}'...";
        searchButton.Enabled = false;

        try
        {
            string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";
            var geoResult = await httpClient.GetFromJsonAsync<GeocodingResponse>(geoUrl);

            if (geoResult?.Results == null || geoResult.Results.Count == 0)
            {
                statusLabel.Text = $"Could not locate '{city}'.";
                MessageBox.Show($"Could not find location coordinates for '{city}'.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var loc = geoResult.Results[0];
            await FetchAndDisplayLocationAsync(loc.Name, loc.Country, loc.Latitude, loc.Longitude);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Network error.";
            MessageBox.Show($"Error retrieving location data: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            searchButton.Enabled = true;
        }
    }

    private async Task SelectHubAsync()
    {
        int index = hubsListBox.SelectedIndex;
        if (index >= 0 && index < GlobalHubs.Length)
        {
            var hub = GlobalHubs[index];
            cityInput.Text = hub.City;
            await FetchAndDisplayLocationAsync(hub.City, hub.Country, hub.Lat, hub.Lon);
        }
    }

    private async Task FetchAndDisplayLocationAsync(string name, string country, double lat, double lon)
    {
        statusLabel.Text = $"Fetching live telemetry for {name}, {country}...";

        try
        {
            // &timezone=auto tells the API to resolve the local time zone and timestamps automatically
            string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,relative_humidity_2m,is_day,weather_code,wind_speed_10m,wind_direction_10m&timezone=auto";
            var report = await httpClient.GetFromJsonAsync<WeatherResult>(weatherUrl);

            if (report != null)
            {
                string formattedTime = FormatLocalTime(report.Current.Time, report.TimezoneAbbreviation);

                weatherInfoLabel.Text =
                    $"Location: {name}, {country}\n\n" +
                    $"Local Time: {formattedTime}\n" +
                    $"Condition: {InterpretWeatherCode(report.Current.WeatherCode)}\n" +
                    $"Temperature: {report.Current.Temperature:F1} °C / {(report.Current.Temperature * 9 / 5 + 32):F1} °F\n" +
                    $"Wind: {report.Current.WindSpeed:F1} km/h ({report.Current.WindDirection}°)\n" +
                    $"Humidity: {report.Current.RelativeHumidity} %\n" +
                    $"Coordinates: {lat:F4}, {lon:F4}\n" +
                    $"Time Zone: {report.Timezone}";

                statusLabel.Text = $"Ready • Updated {name} ({DateTime.Now:HH:mm:ss} Local Host)";
                UpdateAtlasMap(name, lat, lon, report.Current.Temperature, formattedTime);
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Telemetry error.";
            MessageBox.Show($"Failed to fetch weather: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string FormatLocalTime(string rawTime, string? tzAbbrev)
    {
        if (DateTime.TryParse(rawTime, out DateTime parsed))
        {
            return $"{parsed:dd MMM yyyy, HH:mm} ({tzAbbrev ?? "Local"})";
        }
        return rawTime;
    }

    private void UpdateAtlasMap(string title, double lat, double lon, double temp, string localTime)
    {
        string html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta http-equiv='X-UA-Compatible' content='IE=edge' />
            <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
            <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
            <style>
                html, body, #map {{ height: 100%; margin: 0; padding: 0; }}
            </style>
        </head>
        <body>
            <div id='map'></div>
            <script>
                var map = L.map('map').setView([{lat}, {lon}], 7);
                L.tileLayer('https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{
                    attribution: '&copy; OpenStreetMap contributors'
                }}).addTo(map);

                var marker = L.marker([{lat}, {lon}]).addTo(map);
                marker.bindPopup('<b>{title}</b><br>🌡️ {temp:F1}°C<br>🕒 {localTime}').openPopup();
            </script>
        </body>
        </html>";

        mapBrowser.DocumentText = html;
    }

    private static string InterpretWeatherCode(int code) => code switch
    {
        0 => "Clear Sky ☀️",
        1 => "Mainly Clear 🌤️",
        2 => "Partly Cloudy ⛅",
        3 => "Overcast ☁️",
        45 or 48 => "Fog 🌫️",
        51 or 53 or 55 => "Drizzle 🌦️",
        61 or 63 or 65 => "Rain 🌧️",
        71 or 73 or 75 => "Snowfall 🌨️",
        77 => "Snow Grains ❄️",
        80 or 81 or 82 => "Rain Showers 🌧️",
        85 or 86 => "Snow Showers 🌨️",
        95 => "Thunderstorm ⛈️",
        96 or 99 => "Thunderstorm with Hail ⛈️⚡",
        _ => $"Code {code}"
    };
}

public class WeatherResult
{
    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = string.Empty;

    [JsonPropertyName("timezone_abbreviation")]
    public string TimezoneAbbreviation { get; set; } = string.Empty;

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