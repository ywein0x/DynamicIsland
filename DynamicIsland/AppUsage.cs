using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DynamicIsland;

/// <param name="Target">An exe path, or an app id for shell:AppsFolder.</param>
public sealed record AppUsage(string Name, string Target, bool IsExe, int Runs, TimeSpan Focus, ImageSource? Icon, string Arguments = "")
{
    public string FocusText => Focus.TotalHours >= 1 ? $"{Focus.TotalHours:0.#} sa"
        : Focus.TotalMinutes >= 1 ? $"{(int)Focus.TotalMinutes} dk"
        : "";

    public string RunsText => Runs > 0 ? $"{Runs} kez" : "";

    public void Open()
    {
        try
        {
            if (IsExe)
                Process.Start(new ProcessStartInfo(Target, Arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Target) ?? "" });
            else
                Process.Start("explorer.exe", $"shell:AppsFolder\\{Target}");
        }
        catch
        {
            // The app moved or was uninstalled.
        }
    }
}

/// <summary>
/// Most used apps, from Windows' own UserAssist record: how many times an app was started from the
/// shell and how long it was in front. Nothing is tracked by the island itself, and nothing leaves the PC.
/// </summary>
static class AppUsageReader
{
    const string UserAssist = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

    // Shell plumbing, installers and the island itself aren't what anyone means by "my apps".
    static readonly Regex NotApps = new(
        @"ueme_|uninst|setup|install|update|dynamicisland|electron\.exe|\\explorer\.exe|\\cmd\.exe|microsoft\.windows\.|shellexperience|searchapp|taskmgr|systemsettings|\.lnk$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Dictionary<string, Environment.SpecialFolder> KnownFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.SpecialFolder.ProgramFiles,
        ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.SpecialFolder.ProgramFilesX86,
        ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.SpecialFolder.System,
        ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.SpecialFolder.Windows,
    };

    // Almost everyone's top app is a browser, which says nothing; hidden unless the user wants them.
    static readonly Regex Browsers = new(
        @"chrome|msedge|microsoft edge|firefox|opera|brave|vivaldi|yandex|tor browser|waterfox|librewolf|zen browser|safari|\barc\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsBrowser(AppUsage app) => Browsers.IsMatch(app.Name) || Browsers.IsMatch(app.Target);

    /// <summary>The latest reading, for anything that needs it without waiting.</summary>
    public static List<AppUsage> Latest { get; private set; } = [];

    /// <summary>
    /// Reads on its own thread: pulling icons from the shell takes about a second and would freeze the island.
    /// The shell's COM objects need a single-threaded apartment, hence a real thread and not the thread pool.
    /// </summary>
    public static Task<List<AppUsage>> ReadInBackgroundAsync()
    {
        var done = new TaskCompletionSource<List<AppUsage>>();
        var thread = new Thread(() =>
        {
            try
            {
                Latest = Read();
                done.SetResult(Latest);
            }
            catch
            {
                done.SetResult(Latest);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>All recognizable apps, most used first.</summary>
    public static List<AppUsage> Read()
    {
        var apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserAssist);
            if (key == null)
                return [];

            foreach (var encoded in key.GetValueNames())
            {
                if (key.GetValue(encoded) is not byte[] { Length: >= 16 } data)
                    continue;
                var runs = BitConverter.ToInt32(data, 4);
                var focusMs = BitConverter.ToInt32(data, 12);
                var name = Rot13(encoded);
                if ((runs <= 0 && focusMs <= 0) || NotApps.IsMatch(name))
                    continue;
                if (Resolve(name) is not { } app)
                    continue;

                // Updaters leave one entry per version (Discord\app-1.0.x): add them up.
                var focus = TimeSpan.FromMilliseconds(Math.Max(0, focusMs));
                if (apps.TryGetValue(app.Name, out var existing))
                    apps[app.Name] = existing with { Runs = existing.Runs + Math.Max(0, runs), Focus = existing.Focus + focus };
                else
                    apps[app.Name] = app with { Runs = Math.Max(0, runs), Focus = focus };
            }
        }
        catch
        {
            // No record available.
        }
        return [.. apps.Values.OrderByDescending(a => a.Focus).ThenByDescending(a => a.Runs)];
    }

    static AppUsage? Resolve(string entry)
    {
        var path = entry;
        foreach (var (guid, folder) in KnownFolders)
        {
            if (path.StartsWith(guid, StringComparison.OrdinalIgnoreCase))
                path = Environment.GetFolderPath(folder) + path[guid.Length..];
        }

        if (path.Length > 2 && path[1] == ':' && path[2] == '\\')
        {
            if (!File.Exists(path))
                return null;
            string name;
            try { name = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? ""; }
            catch { name = ""; }
            if (name.Length == 0)
                name = Path.GetFileNameWithoutExtension(path);
            // VALORANT's game exe has a poor icon and can't start without the Riot client.
            var riot = Path.Combine(Path.GetPathRoot(path) ?? "", "Riot Games", "Riot Client", "RiotClientServices.exe");
            if (path.Contains(@"\Riot Games\VALORANT\", StringComparison.OrdinalIgnoreCase) && File.Exists(riot))
            {
                var launcher = Path.Combine(Path.GetPathRoot(path) ?? "", "Riot Games", "VALORANT", "live", "VALORANT.exe");
                return new AppUsage("VALORANT", riot, true, 0, TimeSpan.Zero, ShellIcons.ForExe(File.Exists(launcher) ? launcher : path),
                    "--launch-product=valorant --launch-patchline=live");
            }
            return new AppUsage(name, path, true, 0, TimeSpan.Zero, ShellIcons.ForExe(path));
        }

        // Otherwise an app id; it only resolves if the app is still installed.
        var parsing = "shell:AppsFolder\\" + entry;
        var display = ShellIcons.DisplayName(parsing);
        return display == null ? null : new AppUsage(display, entry, false, 0, TimeSpan.Zero, ShellIcons.ForParsingName(parsing));
    }

    static string Rot13(string text) => new(text.Select(c => c switch
    {
        >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
        >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
        _ => c,
    }).ToArray());
}

/// <summary>Real, sharp icons for files and installed apps, from the Windows shell.</summary>
static class ShellIcons
{
    const int IconSize = 128;
    const int SiigbfBiggerSizeOk = 0x1;
    const int SiigbfIconOnly = 0x4;

    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForApp(string appId) => ForParsingName("shell:AppsFolder\\" + appId);

    /// <summary>
    /// The icon embedded in an exe, at the largest size it has. The shell's own version frames small
    /// icons in a white page and blurs them, which is what made some look broken.
    /// </summary>
    public static ImageSource? ForExe(string path)
    {
        var key = "exe:" + path;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
        }
        ImageSource? icon = null;
        var handles = new IntPtr[1];
        var ids = new uint[1];
        try
        {
            if (PrivateExtractIcons(path, 0, IconSize, IconSize, handles, ids, 1, 0) == 1 && handles[0] != IntPtr.Zero)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                icon = source;
            }
        }
        catch
        {
            // Falls through to the shell below.
        }
        finally
        {
            if (handles[0] != IntPtr.Zero)
                DestroyIcon(handles[0]);
        }
        icon ??= ForParsingName(path);
        lock (Cache)
            Cache[key] = icon;
        return icon;
    }

    public static ImageSource? ForParsingName(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;
        }
        ImageSource? icon = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(name, IntPtr.Zero, ref iid, out var item);
            var factory = (IShellItemImageFactory)item;
            if (factory.GetImage(new NativeSize { Width = IconSize, Height = IconSize }, SiigbfIconOnly | SiigbfBiggerSizeOk, out var bitmap) == 0)
            {
                try { icon = ToImage(bitmap); }
                finally { DeleteObject(bitmap); }
            }
        }
        catch
        {
            // Not an item the shell knows.
        }
        lock (Cache)
            Cache[name] = icon;
        return icon;
    }

    public static string? DisplayName(string name)
    {
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(name, IntPtr.Zero, ref iid, out var item);
            item.GetDisplayName(0, out var display);
            return display;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Copies the GDI bitmap top-down with its alpha channel intact.</summary>
    static BitmapSource? ToImage(IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var info) == 0 || info.Width <= 0 || info.Height == 0)
            return null;
        var width = info.Width;
        var height = Math.Abs(info.Height);
        var pixels = new byte[width * height * 4];
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // negative: top row first
            Planes = 1,
            BitCount = 32,
        };
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == 0)
                return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid riid, out IShellItem item);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int width, int height, IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    static extern int GetObject(IntPtr handle, int size, out NativeBitmap info);

    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] pixels, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
