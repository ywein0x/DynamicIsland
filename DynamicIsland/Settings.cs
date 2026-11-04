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
