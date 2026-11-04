using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland;

// System events shown as short-lived pills: volume, battery, lock keys, notifications, copied images,
// clipboard history, downloads, camera/mic, game sessions, system status and updates.
public partial class MainWindow
{
    static readonly Color GreenColor = Color.FromRgb(0x30, 0xD1, 0x58);
    static readonly Color OrangeColor = Color.FromRgb(0xFF, 0x9F, 0x0A);
    static readonly Color RedColor = Color.FromRgb(0xFF, 0x45, 0x3A);
    static readonly Color BlueColor = Color.FromRgb(0x0A, 0x84, 0xFF);


    // Fullscreen apps that aren't games: no "session ended" summary for these.
    static readonly string[] NotGames =
        ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "vlc", "mpc", "potplayer", "wmplayer",
         "powerpnt", "netflix", "explorer", "applicationframehost", "video.ui", "media.player", "obs", "discord"];

    readonly VolumeService _volume = new();
    readonly BatteryService _battery = new();
    readonly NotificationService _notifications = new();
    readonly LockKeyService _locks = new();
    readonly DownloadService _downloads = new();
    readonly PrivacyService _privacy = new();
    readonly DispatcherTimer _downloadFlashTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly List<string> _clipHistory = [];

    DownloadInfo? _download;
    bool _downloadFlash;
    string? _finishedDownload;
    ToastInfo? _lastToast;
    bool _ignoreNextClipboard;
    byte[]? _shotPng;
    int? _batteryLevel;
    bool _batteryCharging;
    UsageWarning? _usageWarning;

    /// <summary>What the warning circle shows.</summary>
    sealed record UsageWarning(string Name, string Value, string Detail, bool Critical);

    // Warn from these up, and stop a little below so it doesn't flicker on the edge.
    const int UsageWarnAt = 90, UsageClearAt = 85, UsageCriticalAt = 97;
    const int GpuHotAt = 85, GpuHotClearAt = 80;

    void InitSystem()
    {
        _volume.Changed += OnVolumeChanged;
        _battery.ChargingStarted += ShowCharging;
        _battery.LowBattery += ShowLowBattery;
        _battery.Updated += ReadBattery;
        _notifications.Received += OnNotification;
        _locks.Toggled += OnLockToggled;
        _privacy.Started += OnPrivacyStarted;
        _privacy.Stopped += (camera, app) =>
        {
            if (!camera && app == DiscordName)
                SetDiscordCall(false);
        };

        _downloads.Started += _ =>
        {
            _downloadFlash = true;
            _downloadFlashTimer.Stop();
            _downloadFlashTimer.Start();
        };
        _downloads.Progress += OnDownloadProgress;
        _downloads.Completed += OnDownloadCompleted;
        _downloadFlashTimer.Tick += (_, _) =>
        {
            _downloadFlashTimer.Stop();
            _downloadFlash = false;
            Refresh();
        };
    }

    void StartSystem()
    {
        try { _volume.Start(); } catch { /* no audio endpoint */ }
        try
        {
            _battery.Start();
            ReadBattery();
        }
        catch
        {
            // No battery API.
        }
        try { _notifications.Start(); } catch { /* notification store unreadable */ }
        try { _locks.Start(); } catch { /* hook refused */ }
        try { _downloads.Start(); } catch { /* no Downloads folder */ }
        try { _privacy.Start(); } catch { /* registry unavailable */ }
    }

    void StopSystem()
    {
        _volume.Dispose();
        _notifications.Dispose();
        _locks.Dispose();
        RemoveClipboardFormatListener(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Fills the generic pill used for camera/mic, game summaries and updates.</summary>
    Action InfoContent(string title, string detail, string? icon = null, Color? iconColor = null, Color? dot = null) => () =>
    {
        InfoIcon.Text = icon ?? "";
        InfoIcon.Visibility = icon != null ? Visibility.Visible : Visibility.Collapsed;
        InfoIcon.Foreground = new SolidColorBrush(iconColor ?? BlueColor);
        InfoDot.Visibility = dot != null ? Visibility.Visible : Visibility.Collapsed;
        if (dot is { } color)
            InfoDot.Fill = new SolidColorBrush(color);
        InfoTitle.Text = title;
        InfoDetail.Text = detail;
    };

    // ---------- Volume ----------

    void OnVolumeChanged(float level, bool muted)
    {
        if (Quiet || !Settings.Get(Feature.Volume))
            return;

        var effective = muted ? 0 : level;
        ShowTransient(Transient.Volume, TimeSpan.FromSeconds(1.6), () =>
        {
            VolumeIcon.Text = effective <= 0 ? "" : effective < 0.34 ? "" : effective < 0.67 ? "" : "";
            VolumeFill.BeginAnimation(WidthProperty, new DoubleAnimation(effective * 160, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        });
    }

    // ---------- Battery ----------

    void ShowCharging(int percent) => ShowBattery("Şarj Oluyor", percent, GreenColor, TimeSpan.FromSeconds(3));

    void ShowLowBattery(int percent) => ShowBattery("Pil Azalıyor", percent, RedColor, TimeSpan.FromSeconds(5));

    void ShowBattery(string title, int percent, Color color, TimeSpan duration)
    {
        if (!Settings.Get(Feature.Battery))
            return;

        ShowTransient(Transient.Battery, duration, () =>
        {
            var solid = new SolidColorBrush(color);
            var dim = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B));
            BatteryTitle.Text = title;
            BatteryText.Text = $"%{percent}";
            BatteryText.Foreground = solid;
            BatteryFill.Background = solid;
            BatteryOutline.BorderBrush = dim;
            BatteryNub.Background = dim;
            BatteryFill.BeginAnimation(WidthProperty, new DoubleAnimation(0, 19.6 * percent / 100, TimeSpan.FromMilliseconds(700))
            {
                BeginTime = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        });
    }

    void ReadBattery()
    {
        _batteryLevel = _battery.CurrentPercent;
        _batteryCharging = _battery.CurrentlyCharging;
        if (Revealed)
            Refresh();
    }

    void ShowBatteryBubble(int level, bool charging, ref BubbleKind kind)
    {
        kind = BubbleKind.Battery;
        var color = charging ? GreenColor : level <= 20 ? RedColor : Colors.White;
        BubbleBatteryArc.Stroke = BubbleBatteryText.Foreground = new SolidColorBrush(color);
        BubbleBatteryArc.Data = RingArc(13, 13, 11.75, level / 100.0);
        BubbleBatteryText.Text = level.ToString();
    }

    string BatteryBubbleTip() => _batteryCharging ? $"Şarj oluyor: %{_batteryLevel}" : $"Pil: %{_batteryLevel}";

    static void OpenBatterySettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:batterysaver") { UseShellExecute = true }); }
        catch { /* settings page unavailable */ }
    }

    static void OpenTaskManager()
    {
        try { Process.Start(new ProcessStartInfo("taskmgr") { UseShellExecute = true }); }
        catch { /* blocked by policy */ }
    }

    // ---------- Caps Lock / Num Lock ----------

    void OnLockToggled(string name, bool on)
    {
        if (Quiet || !Settings.Get(Feature.LockKeys))
            return;

        ShowTransient(Transient.Lock, TimeSpan.FromSeconds(1.5), () =>
        {
            LockName.Text = name;
            LockGlyph.Text = name == "Caps Lock" ? "A" : "1";
            LockState.Text = on ? "Açık" : "Kapalı";
            if (on)
                LockState.Foreground = new SolidColorBrush(GreenColor);
            else
                LockState.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            // Filled badge when on, outline when off.
            if (on)
            {
                LockBadge.SetResourceReference(Border.BackgroundProperty, "Fg");
                LockGlyph.SetResourceReference(TextBlock.ForegroundProperty, "IslandBg");
            }
            else
            {
                LockBadge.Background = Brushes.Transparent;
                LockGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            }
        });
    }

    // ---------- Notifications ----------

    void OnNotification(ToastInfo toast)
    {
        if (!Settings.Get(Feature.Notifications))
            return;

        // Switched off for this app: nothing at all. Do Not Disturb: quietly into the list only.
        Settings.RememberNotificationApp(toast.App);
        if (Settings.IsAppMuted(toast.App))
            return;
        AddToInbox(toast);
        if (Settings.QuietNow)
            return;

        // A second banner while one is up gets a little nudge instead of a fresh entrance.
        if (_transient == Transient.Notification)
            _height.Velocity += 120;

        ShowTransient(Transient.Notification, TimeSpan.FromSeconds(int.Parse(Settings.BannerSeconds)), () =>
        {
            _lastToast = toast;
            NotifApp.Text = toast.App;
            // Private mode: who sent it stays hidden too, in case someone is looking at the screen.
            var hidden = Settings.Get(Feature.HideNotificationText, fallback: false);
            NotifTitle.Text = hidden ? "Yeni bildirim" : toast.Title;
            NotifBody.Text = hidden ? "" : toast.Body;
            NotifBody.Visibility = !hidden && toast.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // The notification's own picture, or the app's real icon; the letter is the last resort.
            var picture = toast.Icon ?? ShellIcons.ForApp(toast.AppId);
            NotifIcon.ImageSource = picture;
            NotifInitial.Text = picture == null ? toast.App[..1].ToUpperInvariant() : "";
            NotifIconBg.Background = picture == null ? new SolidColorBrush(NotificationService.BrandColor(toast.App)) : Brushes.Transparent;
        });
    }

    /// <summary>Opens the app that sent the banner, through its AppUserModelID.</summary>
    void OpenNotificationApp()
    {
        if (_lastToast == null)
            return;
        try { Process.Start("explorer.exe", $"shell:AppsFolder\\{_lastToast.AppId}"); }
        catch { /* app can't be launched this way */ }
    }

    // ---------- Camera & microphone ----------

    void OnPrivacyStarted(bool camera, string app)
    {
        if (!camera && app == DiscordName && DiscordOn)
        {
            SetDiscordCall(true);
            return;
        }
        if (!Settings.Get(Feature.Privacy))
            return;
        ShowTransient(Transient.Privacy, TimeSpan.FromSeconds(3.5), InfoContent(
            camera ? "Kamera kullanılıyor" : "Mikrofon kullanılıyor", app,
            dot: camera ? GreenColor : OrangeColor));
    }

    // ---------- Game sessions ----------

    void ShowGameSummary(TimeSpan played, string? process)
    {
        if (!Settings.Get(Feature.GameSummary) || played < TimeSpan.FromMinutes(3) || process == null)
            return;
        var lower = process.ToLowerInvariant();
        if (NotGames.Any(lower.Contains))
            return;

        var name = process.Split('-', '_')[0];
        var duration = played.TotalHours >= 1 ? $"{(int)played.TotalHours} sa {played.Minutes} dk" : $"{(int)played.TotalMinutes} dk";
        ShowTransient(Transient.Game, TimeSpan.FromSeconds(6), InfoContent("Oyun oturumu bitti", $"{name} · {duration}", ""));
    }

    // ---------- System status ----------

    void ShowStats()
    {
        var sample = _stats.Sample();
        CpuText.Text = $"%{sample.CpuPercent}";
        RamText.Text = $"%{sample.RamPercent}";
        var hasGpu = sample.GpuTemperature != null;
        GpuLabel.Visibility = GpuText.Visibility = hasGpu ? Visibility.Visible : Visibility.Collapsed;
        GpuText.Text = hasGpu ? $"{sample.GpuTemperature}°" : "";

        var warning = WarningFrom(sample, _usageWarning);
        if (warning != _usageWarning)
        {
            _usageWarning = warning;
            Refresh();
        }
    }

    /// <summary>The most pressing thing running high, or null when everything is fine.</summary>
    static UsageWarning? WarningFrom(StatsSample s, UsageWarning? current)
    {
        // Already warning about it: keep going until it has clearly come back down.
        bool High(int? value, string name, int warnAt, int clearAt) =>
            value >= (current?.Name == name ? clearAt : warnAt);

        if (High(s.RamPercent, "RAM", UsageWarnAt, UsageClearAt))
            return new("RAM", s.RamPercent.ToString(), $"RAM çok dolu: %{s.RamPercent}. Görev Yöneticisi için tıkla", s.RamPercent >= UsageCriticalAt);
        if (High(s.GpuTemperature, "GPU°", GpuHotAt, GpuHotClearAt))
            return new("GPU°", s.GpuTemperature.ToString()!, $"Ekran kartı çok sıcak: {s.GpuTemperature}°. Görev Yöneticisi için tıkla", true);
        if (High(s.GpuPercent, "GPU", UsageWarnAt, UsageClearAt))
            return new("GPU", s.GpuPercent.ToString()!, $"Ekran kartı kullanımı yüksek: %{s.GpuPercent}. Görev Yöneticisi için tıkla", s.GpuPercent >= UsageCriticalAt);
        if (High(s.CpuPercent, "CPU", UsageWarnAt, UsageClearAt))
            return new("CPU", s.CpuPercent.ToString(), $"İşlemci kullanımı yüksek: %{s.CpuPercent}. Görev Yöneticisi için tıkla", s.CpuPercent >= UsageCriticalAt);
        return null;
    }

    // ---------- Updates ----------

    async Task CheckForUpdatesAsync(bool manual)
    {
        try
        {
            var release = await Updater.CheckAsync();
            if (release == null)
            {
                _settingsWindow?.SetUpdateStatus("En güncel sürümü kullanıyorsun");
                if (manual && _settingsWindow == null)
                    ShowTransient(Transient.Update, TimeSpan.FromSeconds(3), InfoContent("Dynamic Island güncel", $"v{Updater.CurrentVersion.ToString(3)}", "", GreenColor));
                return;
            }

            _update = release;
            var version = release.Version.ToString(3);
            _settingsWindow?.SetUpdateStatus($"Yeni sürüm hazır: v{version}");
            ShowTransient(Transient.Update, TimeSpan.FromSeconds(8), InfoContent("Güncelleme var", $"v{version} · yüklemek için tıkla", ""));
        }
        catch
        {
            _settingsWindow?.SetUpdateStatus("Denetlenemedi, internet bağlantını kontrol et");
        }
    }

    async void InstallUpdate()
    {
        if (_update == null)
        {
            DismissTransient();
            return;
        }
        InfoTitle.Text = "Güncelleniyor…";
        InfoDetail.Text = $"v{_update.Version.ToString(3)}";
        try
        {
            // The new exe replaces this one and restarts the island.
            await Updater.InstallAsync(_update);
        }
        catch
        {
            Updater.OpenReleasesPage();
            DismissTransient();
        }
    }

    // ---------- Clipboard: copied images and text history ----------

    void StartClipboardListener(IntPtr hwnd)
    {
        HwndSource.FromHwnd(hwnd)?.AddHook(ClipboardHook);
        AddClipboardFormatListener(hwnd);
    }

    IntPtr ClipboardHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            Dispatcher.InvokeAsync(OnClipboardChanged, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    void OnClipboardChanged()
    {
        if (_ignoreNextClipboard)
        {
            _ignoreNextClipboard = false;
            return;
        }

        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null)
                return;
            // Password managers mark secrets this way so clipboard tools leave them alone.
            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore"))
                return;

            if (Clipboard.ContainsImage())
            {
                if (Settings.Get(Feature.Screenshots) && Clipboard.GetImage() is { } image)
                    ShowCopiedImage(image);
            }
            else if (Clipboard.ContainsText() && Settings.Get(Feature.Clipboard))
            {
                RememberClip(Clipboard.GetText());
            }
        }
        catch
        {
            // Another app is holding the clipboard open; skip this change.
        }
    }

    void ShowCopiedImage(BitmapSource image)
    {
        // Snipping Tool images can carry a zeroed alpha channel; drop it, then keep a PNG for saving.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0)));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        var png = buffer.ToArray();

        var preview = new BitmapImage();
        preview.BeginInit();
        preview.CacheOption = BitmapCacheOption.OnLoad;
        preview.DecodePixelWidth = 160;
        preview.StreamSource = new MemoryStream(png);
        preview.EndInit();
        preview.Freeze();

        ShowTransient(Transient.Screenshot, TimeSpan.FromSeconds(5), () =>
        {
            _shotPng = png;
            ShotImage.ImageSource = preview;
        });
    }

    void SaveAndOpenScreenshot()
    {
        if (_shotPng == null)
            return;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Ekran görüntüsü {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            File.WriteAllBytes(path, _shotPng);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Pictures folder unavailable.
        }
    }

    void RememberClip(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        _clipHistory.Remove(text);
        _clipHistory.Insert(0, text);
        while (_clipHistory.Count > int.Parse(Settings.ClipboardSize))
            _clipHistory.RemoveAt(_clipHistory.Count - 1);
    }

    void BuildClipboardList()
    {
        ClipList.Children.Clear();
        if (_clipHistory.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Settings.Get(Feature.Clipboard) ? "Henüz bir şey kopyalanmadı" : "Pano geçmişi Ayarlar'da kapalı",
                FontSize = 12.5,
                Margin = new Thickness(2, 6, 0, 0),
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            ClipList.Children.Add(empty);
            FitClipboard();
            return;
        }

        foreach (var text in _clipHistory)
        {
            var oneLine = text.ReplaceLineEndings(" ").Trim();
            var item = new Button
            {
                Style = (Style)FindResource("ClipItem"),
                Content = oneLine.Length > 90 ? oneLine[..90] + "…" : oneLine,
                Tag = text,
            };
            item.Click += ClipItem_Click;
            ClipList.Children.Add(item);
        }
        FitClipboard();
    }

    void FitClipboard()
    {
        ClipboardContent.Measure(new Size(ClipboardPanel.Width, double.PositiveInfinity));
        ClipboardPanel.Height = Math.Ceiling(ClipboardContent.DesiredSize.Height);
        Refresh();
    }

    void ClipItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string text })
            return;
        try
        {
            _ignoreNextClipboard = true;
            Clipboard.SetText(text);
            RememberClip(text);
        }
        catch
        {
            _ignoreNextClipboard = false;
        }
        Expand(ExpandKind.None);
    }

    // ---------- Downloads ----------

    void OnDownloadProgress(DownloadInfo? download)
    {
        var changed = (_download == null) != (download == null);
        _download = download;
        if (download != null && _transient != Transient.DownloadDone)
        {
            DownloadIcon.Text = "";
            DownloadIcon.Foreground = new SolidColorBrush(BlueColor);
            DownloadName.Text = download.FileName;
            DownloadDetail.Text = download.BytesPerSecond > 0
                ? $"{FormatBytes(download.Bytes)} · {FormatBytes(download.BytesPerSecond)}/sn"
                : FormatBytes(download.Bytes);
        }
        if (changed)
            Refresh();
    }

    void OnDownloadCompleted(string path)
    {
        if (!Settings.Get(Feature.Downloads))
            return;

        ShowTransient(Transient.DownloadDone, TimeSpan.FromSeconds(4), () =>
        {
            _finishedDownload = path;
            DownloadIcon.Text = "";
            DownloadIcon.Foreground = new SolidColorBrush(GreenColor);
            DownloadName.Text = Path.GetFileName(path);
            DownloadDetail.Text = "İndirildi";
        });
    }

    void RevealDownload()
    {
        if (_finishedDownload == null || !File.Exists(_finishedDownload))
            return;
        try { Process.Start("explorer.exe", $"/select,\"{_finishedDownload}\""); }
        catch { /* Explorer unavailable */ }
    }

    void OpenDownloadsFolder()
    {
        if (_downloads.Folder is not { } folder)
            return;
        try { Process.Start("explorer.exe", $"\"{folder}\""); }
        catch { /* Explorer unavailable */ }
    }

    static string FormatBytes(double bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (1L << 10):0} KB",
        _ => $"{bytes:0} B",
    };

    // ---------- Win32 ----------

    const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
