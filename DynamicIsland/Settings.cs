using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>Names of the features that can be switched on and off in Settings.</summary>
static class Feature
{
    public const string Notifications = "Notifications";
    public const string Volume = "Volume";
    public const string Battery = "Battery";
    public const string LockKeys = "LockKeys";
    public const string Screenshots = "Screenshots";
    public const string Clipboard = "Clipboard";
    public const string Downloads = "Downloads";
    public const string Privacy = "Privacy";
    public const string GameSummary = "GameSummary";
    public const string SystemStats = "SystemStats";
    public const string Shelf = "Shelf";
    public const string Marquee = "Marquee";
    public const string RealWaveform = "RealWaveform";


    public const string DiscordActivity = "DiscordActivity";

    // Appearance
    public const string SoftEntrance = "SoftEntrance";
    public const string Shadow = "Shadow";

    // Home menu
    public const string HomeWeather = "HomeWeather";
    public const string HomeCalendar = "HomeCalendar";
    public const string HomeApps = "HomeApps";
    public const string TileTimer = "TileTimer";
    public const string TileInbox = "TileInbox";
    public const string TileFavorites = "TileFavorites";
    public const string TileClipboard = "TileClipboard";
    public const string TileShelf = "TileShelf";
    public const string HideBrowsers = "HideBrowsers";

    // Music
    public const string MusicFlash = "MusicFlash";
    public const string ShowVideos = "ShowVideos";


    // Notifications and sounds
    public const string BatteryBubble = "BatteryBubble";
    public const string UsageWarning = "UsageWarning";
    public const string HideNotificationText = "HideNotificationText";
    public const string TimerSound = "TimerSound";
}

/// <summary>User preferences, stored under HKCU\Software\DynamicIsland.</summary>
static class Settings
{
    const string KeyPath = @"Software\DynamicIsland";

    /// <summary>Raised after any setting is written.</summary>
    public static event Action? Changed;

    /// <summary>Keep the compact music pill on screen instead of hiding it a few seconds after a change.</summary>
    public static bool AlwaysShowMedia
    {
        get => Get(nameof(AlwaysShowMedia), fallback: false);
        set => Set(nameof(AlwaysShowMedia), value);
    }

    /// <summary>"dark", "light" or "system".</summary>
    public static string Theme
    {
        get => GetString(nameof(Theme), "dark");
        set => SetString(nameof(Theme), value);
    }

    /// <summary>Banners stay quiet; notifications only collect in the island's list.</summary>
    public static bool DoNotDisturb
    {
        get => Get(nameof(DoNotDisturb), fallback: false);
        set => Set(nameof(DoNotDisturb), value);
    }

    /// <summary>Apps that have sent a notification, so they can be switched off one by one.</summary>
    public static List<string> NotificationApps =>
        [.. GetString(nameof(NotificationApps), "").Split('|', StringSplitOptions.RemoveEmptyEntries)];

    public static void RememberNotificationApp(string app)
    {
        var apps = NotificationApps;
        if (apps.Contains(app))
            return;
        apps.Add(app);
        SetString(nameof(NotificationApps), string.Join('|', apps));
    }

    public static bool IsAppMuted(string app) => !Get("NotifyApp." + app);

    public static void SetAppMuted(string app, bool muted) => Set("NotifyApp." + app, !muted);

    /// <summary>Accent color as #RRGGBB.</summary>
    public static string Accent
    {
        get => GetString(nameof(Accent), DynamicIsland.Theme.DefaultAccent);
        set => SetString(nameof(Accent), value);
    }

    /// <summary>"small", "normal" or "large".</summary>
    public static string IslandSize
    {
        get => GetString(nameof(IslandSize), "normal");
        set => SetString(nameof(IslandSize), value);
    }

    /// <summary>"sharp", "normal" or "round".</summary>
    public static string IslandCorners
    {
        get => GetString(nameof(IslandCorners), "normal");
        set => SetString(nameof(IslandCorners), value);
    }

    /// <summary>"fast", "normal", "slow" or "off": how long the pointer rests on the top edge before the island appears.</summary>
    public static string RevealDelay
    {
        get => GetString(nameof(RevealDelay), "normal");
        set => SetString(nameof(RevealDelay), value);
    }

    /// <summary>"0", "8" or "16": pixels between the top of the screen and the island.</summary>
    public static string IslandGap
    {
        get => GetString(nameof(IslandGap), "8");
        set => SetString(nameof(IslandGap), value);
    }

    /// <summary>"calm", "normal" or "fast".</summary>
    public static string AnimationSpeed
    {
        get => GetString(nameof(AnimationSpeed), "normal");
        set => SetString(nameof(AnimationSpeed), value);
    }

    /// <summary>Seconds the music pill stays after a song change.</summary>
    public static string MusicFlashSeconds
    {
        get => GetString(nameof(MusicFlashSeconds), "4");
        set => SetString(nameof(MusicFlashSeconds), value);
    }

    /// <summary>Seconds a notification banner stays.</summary>
    public static string BannerSeconds
    {
        get => GetString(nameof(BannerSeconds), "5");
        set => SetString(nameof(BannerSeconds), value);
    }

    /// <summary>"off", "22-08" or "00-08": hours when Do Not Disturb turns itself on.</summary>
    public static string QuietHours
    {
        get => GetString(nameof(QuietHours), "off");
        set => SetString(nameof(QuietHours), value);
    }

    /// <summary>Do Not Disturb right now, by the switch or the quiet hours.</summary>
    public static bool QuietNow
    {
        get
        {
            if (DoNotDisturb)
                return true;
            var hour = DateTime.Now.Hour;
            return QuietHours switch { "22-08" => hour >= 22 || hour < 8, "00-08" => hour < 8, _ => false };
        }
    }

    /// <summary>The user's own Spotify app; empty means not connected.</summary>
    public static string SpotifyClientId
    {
        get => GetString(nameof(SpotifyClientId), "");
        set => SetString(nameof(SpotifyClientId), value);
    }

    /// <summary>How many copied texts to remember.</summary>
    public static string ClipboardSize
    {
        get => GetString(nameof(ClipboardSize), "5");
        set => SetString(nameof(ClipboardSize), value);
    }

    /// <summary>How many apps the home menu shows.</summary>
    public static string TopAppCount
    {
        get => GetString(nameof(TopAppCount), "5");
        set => SetString(nameof(TopAppCount), value);
    }

    public static bool IsAppHidden(string app) => Get("HideApp." + app, fallback: false);

    public static void SetAppHidden(string app, bool hidden) => Set("HideApp." + app, hidden);

    /// <summary>Back to defaults; starting with Windows is stored elsewhere and stays as it is.</summary>
    public static void ResetAll()
    {
        Clear();
        Changed?.Invoke();
    }

    /// <summary>City for the home menu's weather card.</summary>
    public static string WeatherCity
    {
        get => GetString("JarvisCity", "İstanbul"); // stored under its old name so nobody's city resets
        set => SetString("JarvisCity", value);
    }

    /// <summary>Forgets what the removed voice assistant stored, API keys included.</summary>
    public static void RemoveOldAssistantData()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key == null)
            return;
        foreach (var name in new[] { "ApiKey", "GroqKey", "JarvisBrain", "JarvisModel", "JarvisSmart", "Jarvis", "JarvisWakeWord", "JarvisVoice" })
            key.DeleteValue(name, throwOnMissingValue: false);
    }    /// <summary>Features are on unless the user turned them off.</summary>
    public static bool Get(string name, bool fallback = true)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) is int value ? value != 0 : fallback;
    }

    public static void Set(string name, bool value)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        Changed?.Invoke();
    }

    public static string GetString(string name, string fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string ?? fallback;
    }

    public static void SetString(string name, string value)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            key.SetValue(name, value);
        Changed?.Invoke();
    }

    /// <summary>Removes every stored preference (used by uninstall).</summary>
    public static void Clear() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}
