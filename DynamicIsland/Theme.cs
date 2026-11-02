using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>Light and dark palettes (iOS system colors), swapped in as dynamic resources.</summary>
static class Theme
{
    public static bool IsLight => Settings.Theme switch
    {
        "light" => true,
        "system" => SystemUsesLightTheme(),
        _ => false,
    };

    public static void Apply(ResourceDictionary resources)
    {
        var light = IsLight;
        Set(resources, "IslandBg", light ? "#FFFFFF" : "#000000");
        Set(resources, "Fg", light ? "#000000" : "#FFFFFF");
        Set(resources, "SecondaryText", light ? "#6C6C70" : "#8E8E93");
        Set(resources, "SubtleFg", light ? "#3A3A3C" : "#C7C7CC");
        Set(resources, "TrackBrush", light ? "#D1D1D6" : "#3A3A3C");
        Set(resources, "ArtPlaceholder", light ? "#E5E5EA" : "#2C2C2E");
        Set(resources, "ClipBg", light ? "#F2F2F7" : "#1C1C1E");
        Set(resources, "ClipHover", light ? "#E5E5EA" : "#2C2C2E");
        Set(resources, "WindowBg", light ? "#F2F2F7" : "#000000");
        Set(resources, "GroupBg", light ? "#FFFFFF" : "#1C1C1E");
        Set(resources, "SeparatorBrush", light ? "#C6C6C8" : "#38383A");
    }

    static void Set(ResourceDictionary resources, string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        resources[key] = brush;
    }

    static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
}
