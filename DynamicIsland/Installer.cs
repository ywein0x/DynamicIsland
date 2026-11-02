using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>
/// The published exe installs itself: run it from anywhere and it copies itself to
/// %LocalAppData%\Programs\DynamicIsland, adds a Start menu shortcut, starts with Windows
/// and appears in Settings › Apps for uninstalling. No admin rights needed.
/// </summary>
static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland";

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DynamicIsland");

    public static string InstalledExe => Path.Combine(InstallDir, "DynamicIsland.exe");

    static string CurrentExe => Environment.ProcessPath!;

    static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Dynamic Island.lnk");

    /// <summary>Running from a build folder while developing: never install from there.</summary>
    public static bool IsDevBuild => CurrentExe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => string.Equals(Path.GetFullPath(CurrentExe), InstalledExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Installs (or updates) from a downloaded exe. Returns true when the installed copy was
    /// started and this process should exit.
    /// </summary>
    public static bool InstallIfNeeded()
    {
        if (IsDevBuild || IsInstalled)
            return false;

        StopOtherInstances();
        Directory.CreateDirectory(InstallDir);
        File.Copy(CurrentExe, InstalledExe, overwrite: true);
        Register();
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
        return true;
    }

    static void Register()
    {
        CreateShortcut(Shortcut, InstalledExe);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Dynamic Island");
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("DisplayVersion", Updater.CurrentVersion.ToString(3));
            key.SetValue("Publisher", "Dynamic Island");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        // Starts with Windows by default; the user can turn it off in Settings or the tray menu.
        Autostart.SetFor(InstalledExe, true);
    }

    public static void Uninstall()
    {
        StopOtherInstances();
        Autostart.SetFor(InstalledExe, false);
        TryDelete(Shortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        Settings.Clear();

        // The running exe can't delete itself; a short-lived shell finishes the job after we exit.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    static void StopOtherInstances()
    {
        var me = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("DynamicIsland").Where(p => p.Id != me))
        {
            try
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            catch
            {
                // Already gone.
            }
        }
    }

    static void CreateShortcut(string path, string target)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = "Dynamic Island";
            link.Save();
        }
        catch
        {
            // Shortcut is a convenience; the app works without it.
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* already gone */ }
    }
}
