using System.Diagnostics;
using System.Globalization;

namespace DynamicIsland;

/// <summary>Finds an installed app by name in the Start menu's app list (desktop and Store apps) and opens it.</summary>
static class AppLauncher
{
    static readonly CultureInfo Turkish = new("tr-TR");

    /// <summary>Opens the best match and returns its display name, or null if nothing matched.</summary>
    public static string? Open(string name)
    {
        var wanted = Normalize(name);
        if (wanted.Length == 0)
            return null;

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null)
            return null;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic apps = shell.NameSpace("shell:AppsFolder");

        string? bestName = null, bestId = null;
        var bestScore = int.MaxValue;
        foreach (dynamic item in apps.Items())
        {
            string display = item.Name;
            string id = item.Path;
            var candidate = Normalize(display);
            var score = candidate == wanted ? 0 : candidate.StartsWith(wanted) ? 1 : candidate.Contains(wanted) ? 2 : int.MaxValue;
            if (score < bestScore || score == bestScore && score != int.MaxValue && display.Length < bestName!.Length)
            {
                (bestScore, bestName, bestId) = (score, display, id);
            }
        }

        if (bestId == null)
            return null;
        Process.Start("explorer.exe", $"shell:AppsFolder\\{bestId}");
        return bestName;
    }

    static string Normalize(string text) =>
        new(text.ToLower(Turkish).Where(char.IsLetterOrDigit).ToArray());
}
