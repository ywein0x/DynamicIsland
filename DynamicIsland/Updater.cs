using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DynamicIsland;

public sealed record UpdateRelease(Version Version, string DownloadUrl, string PageUrl);

/// <summary>Checks the GitHub releases of the project and swaps in a newer exe.</summary>
static class Updater
{
    public const string Repository = "ywein0x/DynamicIsland";

    public static Version CurrentVersion => Normalize(Assembly.GetEntryAssembly()!.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>The newest release if it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateRelease?> CheckAsync()
    {
        using var http = CreateClient();
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || Normalize(latest) <= CurrentVersion)
            return null;

        var asset = root.GetProperty("assets").EnumerateArray()
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(url => url != null && url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (asset == null)
            return null;

        return new UpdateRelease(Normalize(latest), asset, root.GetProperty("html_url").GetString() ?? "");
    }

    /// <summary>
    /// Downloads the new exe and runs it; the new exe installs itself over this one
    /// (see <see cref="Installer.InstallIfNeeded"/>) and restarts the island.
    /// </summary>
    public static async Task InstallAsync(UpdateRelease release)
    {
        var folder = Path.Combine(Path.GetTempPath(), "DynamicIslandUpdate");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"DynamicIsland-{release.Version}.exe");

        using (var http = CreateClient())
        await using (var download = await http.GetStreamAsync(release.DownloadUrl))
        await using (var target = File.Create(file))
            await download.CopyToAsync(target);

        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }

    public static void OpenReleasesPage() =>
        Process.Start(new ProcessStartInfo($"https://github.com/{Repository}/releases") { UseShellExecute = true });

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DynamicIsland/{CurrentVersion}");
        return http;
    }

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
