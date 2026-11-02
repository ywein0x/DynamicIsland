using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>
/// Tells when an app starts using the camera or microphone, from the same registry records
/// Windows uses for its own privacy indicator. Events fire on the UI thread.
/// </summary>
public sealed class PrivacyService
{
    const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<string, HashSet<string>> _inUse = new() { ["webcam"] = [], ["microphone"] = [] };

    /// <summary>(isCamera, app name) when an app starts using the device.</summary>
    public event Action<bool, string>? Started;

    public void Start()
    {
        foreach (var kind in _inUse.Keys)
            _inUse[kind] = ActiveApps(kind);
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
    }

    void Poll()
    {
        foreach (var kind in _inUse.Keys.ToList())
        {
            var now = ActiveApps(kind);
            foreach (var app in now.Except(_inUse[kind]))
                Started?.Invoke(kind == "webcam", app);
            _inUse[kind] = now;
        }
    }

    static HashSet<string> ActiveApps(string kind)
    {
        var apps = new HashSet<string>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{kind}");
            if (root == null)
                return apps;

            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key == null)
                    continue;

                if (name == "NonPackaged")
                {
                    // Desktop apps are keyed by their exe path with '#' instead of '\'.
                    foreach (var exe in key.GetSubKeyNames())
                    {
                        using var app = key.OpenSubKey(exe);
                        if (app != null && InUse(app))
                            apps.Add(Path.GetFileNameWithoutExtension(exe.Replace('#', '\\')));
                    }
                }
                else if (InUse(key))
                {
                    apps.Add(name);
                }
            }
        }
        catch
        {
            // Registry unavailable; report nothing.
        }

        return apps.Select(NotificationService.AppName).ToHashSet();
    }

    // In use while a session has started and not yet stopped.
    static bool InUse(RegistryKey key) =>
        key.GetValue("LastUsedTimeStart") is long start && start > 0
        && key.GetValue("LastUsedTimeStop") is long stop && stop == 0;
}
