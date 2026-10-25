using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

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
    
    private readonly WebView2 mapWebView = new();

    private readonly ListView forecastListView = new();
    private readonly ListView rainTrendListView = new();

    // Track active coordinates for Refresh and Copy tools
    private double currentActiveLat = 21.0285;
    private double currentActiveLon = 105.8542;
    private string currentActiveName = "Hanoi";
    private string currentActiveCountry = "Vietnam";

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
        Text = "Global Weather Tracker & Chromium Atlas Dashboard";
        Width = 1200;
        Height = 860;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        // --- 1. Top Search Bar ---
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

        statusLabel = new() { Left = 550, Top = 22, Width = 600, ForeColor = Color.DarkSlateGray };
        topPanel.Controls.AddRange(new Control[] { searchLabel, cityInput, searchButton, statusLabel });

        // --- 2. Left Telemetry & Hubs Panel ---
        Panel leftPanel = new()
        {
            Dock = DockStyle.Left,
            Width = 330,
            BackColor = Color.FromArgb(249, 250, 251),
            Padding = new Padding(12)
        };

        Label telemetryHeader = new()
        {
            Text = "Live Weather & Local Time",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 28
        };

        weatherInfoLabel = new()
        {
            Text = "Search for a city, double-click a hub, or click anywhere directly on the map to inspect weather telemetry.",
            Dock = DockStyle.Top,
            Height = 210,
            Font = new Font("Segoe UI", 9.5f),
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

        // --- 3. Bottom Analytics Panel ---
        Panel bottomPanel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 240,
            BackColor = Color.White
        };

        TabControl analyticsTabs = new()
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f)
        };

        TabPage forecastTab = new("📅 7-Day Extended Forecast");
        forecastListView.Dock = DockStyle.Fill;
        forecastListView.View = View.Details;
        forecastListView.FullRowSelect = true;
        forecastListView.GridLines = true;
        forecastListView.Columns.Add("Date / Day", 130);
        forecastListView.Columns.Add("Condition", 180);
        forecastListView.Columns.Add("Min Temp", 90);
        forecastListView.Columns.Add("Max Temp", 90);
        forecastListView.Columns.Add("Precipitation", 110);
        forecastListView.Columns.Add("Rain Chance", 110);
        forecastTab.Controls.Add(forecastListView);

        TabPage rainTab = new("🌧️ 24-Hour Rain & Precipitation Trend");
        rainTrendListView.Dock = DockStyle.Fill;
        rainTrendListView.View = View.Details;
        rainTrendListView.FullRowSelect = true;
        rainTrendListView.GridLines = true;
        rainTrendListView.Columns.Add("Time", 100);
        rainTrendListView.Columns.Add("Temp", 80);
        rainTrendListView.Columns.Add("Precipitation", 110);
        rainTrendListView.Columns.Add("Probability", 100);
        rainTrendListView.Columns.Add("24-Hour Probability Histogram", 350);
        rainTab.Controls.Add(rainTrendListView);

        analyticsTabs.TabPages.Add(forecastTab);
        analyticsTabs.TabPages.Add(rainTab);
        bottomPanel.Controls.Add(analyticsTabs);

        // --- 4. Center Map Panel ---
        mapWebView.Dock = DockStyle.Fill;

        // Assembly of Main Controls (Order matters for Docking)
        Controls.Add(mapWebView);
        Controls.Add(bottomPanel);
        Controls.Add(leftPanel);
        Controls.Add(topPanel);

        // Initialize Menu Bar at the very top
        InitializeMenuBar();

        // Initialize WebView2 and load default hub
        Load += async (s, e) =>
        {
            try
            {
                await mapWebView.EnsureCoreWebView2Async();
                
                mapWebView.CoreWebView2.WebMessageReceived += async (sender, args) =>
                {
                    try
                    {
                        string jsonString = args.WebMessageAsJson;
                        var clickData = JsonSerializer.Deserialize<MapClickPayload>(jsonString);
                        if (clickData != null)
                        {
                            await OnMapClickedAsync(clickData.Lat, clickData.Lon);
                        }
                    }
                    catch (Exception ex)
                    {
                        statusLabel.Text = $"Map click error: {ex.Message}";
                    }
                };

                await FetchAndDisplayLocationAsync("Hanoi", "Vietnam", 21.0285, 105.8542);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not initialize WebView2: {ex.Message}", "WebView2 Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }

    // ==========================================
    // MENU BAR INITIALIZATION
    // ==========================================
    private void InitializeMenuBar()
    {
        MenuStrip menuStrip = new() { BackColor = Color.FromArgb(240, 243, 246) };

        // File Menu
        ToolStripMenuItem fileMenu = new("&File");
        ToolStripMenuItem refreshItem = new("&Refresh Telemetry", null, async (s, e) =>
        {
            await FetchAndDisplayLocationAsync(currentActiveName, currentActiveCountry, currentActiveLat, currentActiveLon);
        })
        {
            ShortcutKeys = Keys.F5
        };

        ToolStripMenuItem exitItem = new("E&xit", null, (s, e) => Application.Exit())
        {
            ShortcutKeys = Keys.Alt | Keys.F4
        };
        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { refreshItem, new ToolStripSeparator(), exitItem });

        // Tools Menu
        ToolStripMenuItem toolsMenu = new("&Tools");
        ToolStripMenuItem copyCoordsItem = new("Copy Current &Coordinates", null, (s, e) =>
        {
            Clipboard.SetText($"{currentActiveLat:F4}, {currentActiveLon:F4}");
            MessageBox.Show($"Copied ({currentActiveLat:F4}, {currentActiveLon:F4}) to clipboard!", "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        toolsMenu.DropDownItems.Add(copyCoordsItem);

        // Help Menu
        ToolStripMenuItem helpMenu = new("&Help");
        ToolStripMenuItem versionItem = new("&About & Version", null, (s, e) => ShowAboutDialog());
        helpMenu.DropDownItems.Add(versionItem);

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, toolsMenu, helpMenu });
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private static void ShowAboutDialog()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName();
        string version = asm.Version?.ToString(3) ?? "1.0.0";
        string runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        string osArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();

        string message = 
            $"Global Weather Tracker\n" +
            $"Version: {version}\n" +
            $"Runtime: {runtime} ({osArch})\n" +
            $"Map Engine: Microsoft Edge WebView2 (Chromium)\n\n" +
            $"Data Providers:\n" +
            $"• Open-Meteo Weather API\n" +
            $"• Esri ArcGIS & OpenStreetMap Contributors";

        MessageBox.Show(message, "About Global Weather Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task OnMapClickedAsync(double lat, double lon)
    {
        statusLabel.Text = $"Pin dropped at ({lat:F4}, {lon:F4}). Inspecting telemetry...";
        string locationName = $"Coordinates ({lat:F3}, {lon:F3})";
        string country = "Earth";

        cityInput.Text = $"{lat:F4}, {lon:F4}";
        await FetchAndDisplayLocationAsync(locationName, country, lat, lon);
    }

    private async Task SearchCityWeatherAsync()
    {
        string input = cityInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show("Please enter a city name to search.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var parts = input.Split(',');
        if (parts.Length == 2 && double.TryParse(parts[0], out double lat) && double.TryParse(parts[1], out double lon))
        {
            await FetchAndDisplayLocationAsync($"Coordinates ({lat:F3}, {lon:F3})", "Earth", lat, lon);
            return;
        }

        statusLabel.Text = $"Geocoding '{input}'...";
        searchButton.Enabled = false;

        try
        {
            string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(input)}&count=1&language=en&format=json";
            var geoResult = await httpClient.GetFromJsonAsync<GeocodingResponse>(geoUrl);

            if (geoResult?.Results == null || geoResult.Results.Count == 0)
            {
                statusLabel.Text = $"Could not locate '{input}'.";
                MessageBox.Show($"Could not find location coordinates for '{input}'.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        currentActiveLat = lat;
        currentActiveLon = lon;
        currentActiveName = name;
        currentActiveCountry = country;

        statusLabel.Text = $"Fetching live telemetry for {name}...";

        try
        {
            string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,relative_humidity_2m,is_day,weather_code,wind_speed_10m,wind_direction_10m&hourly=temperature_2m,precipitation,precipitation_probability&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max&timezone=auto";
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
                    $"Timezone: {report.Timezone}";

                statusLabel.Text = $"Ready • Updated {name} ({DateTime.Now:HH:mm:ss} Local Host)";
                UpdateAtlasMap(name, lat, lon, report.Current.Temperature, formattedTime);
                UpdateForecastTable(report.Daily);
                UpdateRainTrendTable(report.Hourly, report.Current.Time);
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Telemetry error.";
            MessageBox.Show($"Failed to fetch weather: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateForecastTable(DailyForecast? daily)
    {
        forecastListView.Items.Clear();
        if (daily == null || daily.Time.Count == 0) return;

        for (int i = 0; i < daily.Time.Count; i++)
        {
            string dateStr = daily.Time[i];
            if (DateTime.TryParse(dateStr, out DateTime dt))
            {
                dateStr = dt.ToString("ddd, dd MMM");
            }

            string condition = InterpretWeatherCode(daily.WeatherCode.Count > i ? daily.WeatherCode[i] : 0);
            string minTemp = daily.TemperatureMin.Count > i ? $"{daily.TemperatureMin[i]:F1} °C" : "--";
            string maxTemp = daily.TemperatureMax.Count > i ? $"{daily.TemperatureMax[i]:F1} °C" : "--";
            string precip = daily.PrecipitationSum.Count > i ? $"{daily.PrecipitationSum[i]:F1} mm" : "--";
            string prob = (daily.PrecipitationProbabilityMax != null && daily.PrecipitationProbabilityMax.Count > i)
                ? $"{daily.PrecipitationProbabilityMax[i]} %"
                : "--";

            ListViewItem item = new(dateStr);
            item.SubItems.AddRange(new[] { condition, minTemp, maxTemp, precip, prob });
            forecastListView.Items.Add(item);
        }
    }

    private void UpdateRainTrendTable(HourlyForecast? hourly, string currentTimeStr)
    {
        rainTrendListView.Items.Clear();
        if (hourly == null || hourly.Time.Count == 0) return;

        int startIndex = 0;
        if (DateTime.TryParse(currentTimeStr, out DateTime currentDt))
        {
            for (int i = 0; i < hourly.Time.Count; i++)
            {
                if (DateTime.TryParse(hourly.Time[i], out DateTime hDt) && hDt >= currentDt.AddMinutes(-30))
                {
                    startIndex = i;
                    break;
                }
            }
        }

        int count = Math.Min(24, hourly.Time.Count - startIndex);
        for (int i = startIndex; i < startIndex + count; i++)
        {
            string timeStr = hourly.Time[i];
            if (DateTime.TryParse(timeStr, out DateTime dt))
            {
                timeStr = dt.ToString("HH:mm (ddd)");
            }

            string temp = hourly.Temperature.Count > i ? $"{hourly.Temperature[i]:F1} °C" : "--";
            double precipVal = hourly.Precipitation.Count > i ? hourly.Precipitation[i] : 0.0;
            string precip = $"{precipVal:F1} mm";

            int probVal = (hourly.PrecipitationProbability != null && hourly.PrecipitationProbability.Count > i)
                ? hourly.PrecipitationProbability[i]
                : 0;

            string prob = $"{probVal} %";
            string visualBar = GenerateVisualBar(probVal);

            ListViewItem item = new(timeStr);
            item.SubItems.AddRange(new[] { temp, precip, prob, visualBar });
            rainTrendListView.Items.Add(item);
        }
    }

    private static string GenerateVisualBar(int percent)
    {
        int totalSegments = 14;
        int filled = (int)Math.Round((percent / 100.0) * totalSegments);
        filled = Math.Clamp(filled, 0, totalSegments);
        return $"{new string('█', filled)}{new string('░', totalSegments - filled)}  {percent,3}%";
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
        if (mapWebView.CoreWebView2 == null) return;

        string html = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8' />
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
            <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
            <style>
                html, body, #map {{ height: 100%; margin: 0; padding: 0; background: #e5e3df; cursor: crosshair; }}
            </style>
        </head>
        <body>
            <div id='map'></div>
            <script>
                var map = L.map('map').setView([{lat}, {lon}], 7);
                
                L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{{z}}/{{y}}/{{x}}', {{
                    attribution: 'Tiles &copy; Esri &mdash; Esri, DeLorme, NAVTEQ, TomTom, Intermap, USGS',
                    maxZoom: 18
                }}).addTo(map);

                var marker = L.marker([{lat}, {lon}]).addTo(map);
                marker.bindPopup('<b>{title}</b><br>🌡️ {temp:F1}°C<br>🕒 {localTime}').openPopup();

                map.on('click', function(e) {{
                    var clickedLat = e.latlng.lat;
                    var clickedLng = e.latlng.lng;

                    if (window.chrome && window.chrome.webview) {{
                        window.chrome.webview.postMessage({{
                            lat: clickedLat,
                            lon: clickedLng
                        }});
                    }}
                }});
            </script>
        </body>
        </html>";

        mapWebView.NavigateToString(html);
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

public class MapClickPayload
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}

public class WeatherResult
{
    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = string.Empty;

    [JsonPropertyName("timezone_abbreviation")]
    public string TimezoneAbbreviation { get; set; } = string.Empty;

    [JsonPropertyName("current")]
    public CurrentWeather Current { get; set; } = new();

    [JsonPropertyName("daily")]
    public DailyForecast? Daily { get; set; }

    [JsonPropertyName("hourly")]
    public HourlyForecast? Hourly { get; set; }
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

public class DailyForecast
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = [];

    [JsonPropertyName("weather_code")]
    public List<int> WeatherCode { get; set; } = [];

    [JsonPropertyName("temperature_2m_max")]
    public List<double> TemperatureMax { get; set; } = [];

    [JsonPropertyName("temperature_2m_min")]
    public List<double> TemperatureMin { get; set; } = [];

    [JsonPropertyName("precipitation_sum")]
    public List<double> PrecipitationSum { get; set; } = [];

    [JsonPropertyName("precipitation_probability_max")]
    public List<int>? PrecipitationProbabilityMax { get; set; }
}

public class HourlyForecast
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = [];

    [JsonPropertyName("temperature_2m")]
    public List<double> Temperature { get; set; } = [];

    [JsonPropertyName("precipitation")]
    public List<double> Precipitation { get; set; } = [];

    [JsonPropertyName("precipitation_probability")]
    public List<int>? PrecipitationProbability { get; set; }
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