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

    /// <summary>Features are on unless the user turned them off.</summary>
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
