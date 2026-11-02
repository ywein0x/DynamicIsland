using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace DynamicIsland;

/// <param name="AppId">AppUserModelID, used to open the app when the banner is clicked.</param>
public sealed record ToastInfo(string App, string AppId, string Title, string Body, BitmapSource? Icon);

/// <summary>
/// Watches the Windows notification store for new toasts. Unpackaged apps can't use
/// UserNotificationListener, but the store is a per-user SQLite file we can read.
/// <see cref="Received"/> is raised on the UI thread.
/// </summary>
public sealed class NotificationService : IDisposable
{
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    SqliteConnection? _db;
    long _lastOrder;

    public event Action<ToastInfo>? Received;

    static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Microsoft\Windows\Notifications\wpndatabase.db");

    public void Start()
    {
        if (!File.Exists(DbPath))
            return;

        _db = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly");
        _db.Open();

        // Only toasts that arrive from now on.
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT IFNULL(MAX([Order]), 0) FROM Notification";
            _lastOrder = (long)cmd.ExecuteScalar()!;
        }

        _poll.Tick += (_, _) => Poll();
        _poll.Start();
    }

    void Poll()
    {
        try
        {
            using var cmd = _db!.CreateCommand();
            cmd.CommandText = """
                SELECT n.[Order], n.Payload, h.PrimaryId
                FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId
                WHERE n.[Order] > $last AND n.Type = 'toast'
                ORDER BY n.[Order]
                """;
            cmd.Parameters.AddWithValue("$last", _lastOrder);

            ToastInfo? latest = null;
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    _lastOrder = reader.GetInt64(0);
                    if (!reader.IsDBNull(1))
                        latest = Parse((byte[])reader[1], reader.GetString(2)) ?? latest;
                }
            }

            if (latest != null)
                Received?.Invoke(latest);
        }
        catch
        {
            // Store busy or locked for a moment; try again on the next tick.
        }
    }

    static ToastInfo? Parse(byte[] payload, string appId)
    {
        // The island shows its own screenshot preview; skip the Snipping Tool's toast.
        if (appId.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase)
            || appId.Contains("SnippingTool", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var xml = XDocument.Parse(Encoding.UTF8.GetString(payload).Trim('﻿', '\0'));
            var binding = xml.Descendants("binding").FirstOrDefault();
            if (binding == null)
                return null;

            var texts = binding.Elements("text").Select(t => t.Value.Trim()).Where(t => t.Length > 0).ToList();
            if (texts.Count == 0)
                return null;

            var images = binding.Elements("image").ToList();
            var logo = images.FirstOrDefault(i => (string?)i.Attribute("placement") == "appLogoOverride") ?? images.FirstOrDefault();

            return new ToastInfo(AppName(appId), appId, texts[0], string.Join(" ", texts.Skip(1)), LoadIcon((string?)logo?.Attribute("src")));
        }
        catch
        {
            return null;
        }
    }

    static readonly (string Key, string Name, string Color)[] KnownApps =
    [
        ("whatsapp", "WhatsApp", "#25D366"),
        ("discord", "Discord", "#5865F2"),
        ("telegram", "Telegram", "#2AABEE"),
        ("spotify", "Spotify", "#1DB954"),
        ("chrome", "Chrome", "#4285F4"),
        ("msedge", "Edge", "#0C59A4"),
        ("brave", "Brave", "#FB542B"),
        ("opera", "Opera GX", "#FA1E4E"),
        ("teams", "Teams", "#5059C9"),
        ("outlook", "Outlook", "#0F78D4"),
        ("steam", "Steam", "#1B2838"),
        ("instagram", "Instagram", "#E1306C"),
    ];

    /// <summary>Turns an app id such as "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App" into "WhatsApp".</summary>
    public static string AppName(string appId)
    {
        foreach (var app in KnownApps)
            if (appId.Contains(app.Key, StringComparison.OrdinalIgnoreCase))
                return app.Name;

        var name = appId.Split('!')[0].Split('_')[0];
        name = name[(name.LastIndexOfAny(['.', '\\']) + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.Length > 0 ? name : "Bildirim";
    }

    public static Color BrandColor(string appName)
    {
        foreach (var app in KnownApps)
            if (app.Name == appName)
                return (Color)ColorConverter.ConvertFromString(app.Color);
        return Color.FromRgb(0x0A, 0x84, 0xFF);
    }

    static BitmapSource? LoadIcon(string? src)
    {
        if (string.IsNullOrEmpty(src))
            return null;
        try
        {
            var path = Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : src;
            if (!Path.IsPathRooted(path) || !File.Exists(path))
                return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 84;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _poll.Stop();
        _db?.Dispose();
    }
}
