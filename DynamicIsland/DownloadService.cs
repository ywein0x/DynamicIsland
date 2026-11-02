using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicIsland;

public sealed record DownloadInfo(string FileName, long Bytes, double BytesPerSecond);

/// <summary>
/// Follows browser downloads by watching the Downloads folder for in-progress files
/// (.crdownload for Chrome/Edge/Brave/Opera, .part for Firefox). Browsers don't expose the
/// total size, so this reports bytes so far and speed rather than a percentage.
/// Events fire on the UI thread.
/// </summary>
public sealed class DownloadService
{
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<string, (long Size, DateTime At, double Speed)> _active = new(StringComparer.OrdinalIgnoreCase);
    string? _folder;

    /// <summary>The Downloads folder being watched.</summary>
    public string? Folder => _folder;

    public event Action<DownloadInfo>? Started;
    /// <summary>The most recent active download, or null when nothing is downloading.</summary>
    public event Action<DownloadInfo?>? Progress;
    /// <summary>Full path of a finished file.</summary>
    public event Action<string>? Completed;

    public void Start()
    {
        _folder = DownloadsFolder();
        if (!Directory.Exists(_folder))
            return;

        // Downloads already running when the island starts are followed, but don't announce themselves.
        foreach (var path in PartialFiles())
            _active[path] = (SafeLength(path), DateTime.Now, 0);

        _poll.Tick += (_, _) => Poll();
        _poll.Start();
    }

    void Poll()
    {
        var partials = PartialFiles();

        foreach (var gone in _active.Keys.Except(partials, StringComparer.OrdinalIgnoreCase).ToList())
        {
            _active.Remove(gone);
            var final = FinalPath(gone);
            // Renamed to its real name means finished; vanished entirely means cancelled.
            if (File.Exists(final) && SafeLength(final) > 0)
                Completed?.Invoke(final);
        }

        DownloadInfo? latest = null;
        var now = DateTime.Now;
        foreach (var path in partials)
        {
            var size = SafeLength(path);
            if (!_active.TryGetValue(path, out var previous))
            {
                _active[path] = (size, now, 0);
                latest = new DownloadInfo(Path.GetFileName(FinalPath(path)), size, 0);
                Started?.Invoke(latest);
                continue;
            }

            var seconds = (now - previous.At).TotalSeconds;
            var speed = seconds > 0 ? Math.Max(0, (size - previous.Size) / seconds) : previous.Speed;
            speed = previous.Speed == 0 ? speed : previous.Speed * 0.6 + speed * 0.4; // smooth jumpy readings
            _active[path] = (size, now, speed);
            latest = new DownloadInfo(Path.GetFileName(FinalPath(path)), size, speed);
        }

        Progress?.Invoke(latest);
    }

    List<string> PartialFiles()
    {
        try
        {
            return Directory.EnumerateFiles(_folder!)
                .Where(f => f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    static string FinalPath(string partial) => partial[..partial.LastIndexOf('.')];

    static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    static string DownloadsFolder()
    {
        var downloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        return SHGetKnownFolderPath(downloadsId, 0, IntPtr.Zero, out var path) == 0
            ? path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token,
        [MarshalAs(UnmanagedType.LPWStr)] out string path);
}
