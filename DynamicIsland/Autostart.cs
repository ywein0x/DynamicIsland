using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>Per-user "start with Windows" via the HKCU Run key.</summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "DynamicIsland";

    /// <summary>Whether this exe starts with Windows.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value == Command(Environment.ProcessPath!);
        }
        set => SetFor(Environment.ProcessPath!, value);
    }

    public static void SetFor(string exePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command(exePath));
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    static string Command(string exePath) => $"\"{exePath}\"";
}
