using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DynamicIsland;

// Home menu: clicking the island opens what is happening right now (music, timer, download, or the
// clock when idle) above shortcuts to everything else.
public partial class MainWindow
{
    static readonly CultureInfo HomeCulture = new("tr-TR");

    readonly DispatcherTimer _homeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    bool _fromHome;   // the open card was reached from Home; closing it goes back there

    void InitHome() => _homeTimer.Tick += (_, _) => UpdateHome();

    /// <summary>Fills the live cards and fits the island around them.</summary>
    void UpdateHome()
    {
        if (_expand != ExpandKind.Home)
        {
            _homeTimer.Stop();
            return;
        }

        var music = _state;
        HomeMusicCard.Visibility = music != null ? Visibility.Visible : Visibility.Collapsed;
        if (music != null)
        {
            HomeArt.ImageSource = CompactArt.ImageSource;
            HomeTitle.Text = music.Title;
            HomeArtist.Text = music.Artist;
            HomePlayIcon.Visibility = music.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
            HomePauseIcon.Visibility = music.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        var timerOn = _timer is TimerState.Running or TimerState.Paused;
        HomeTimerCard.Visibility = timerOn ? Visibility.Visible : Visibility.Collapsed;
        if (timerOn)
        {
            HomeTimerText.Text = TimerCompactText.Text;
            HomeTimerRing.Data = TimerRingArc.Data;
            HomeTimerLabel.Text = _timer == TimerState.Paused ? "Zamanlayıcı · duraklatıldı" : "Zamanlayıcı";
        }

        var download = Settings.Get(Feature.Downloads) ? _download : null;
        HomeDownloadCard.Visibility = download != null ? Visibility.Visible : Visibility.Collapsed;
        if (download != null)
        {
            HomeDownloadName.Text = download.FileName;
            HomeDownloadDetail.Text = DownloadDetail.Text;
        }

        HomeDiscordCard.Visibility = DiscordOn && (_discordCall || _discordUnread > 0 || _discordRunning) ? Visibility.Visible : Visibility.Collapsed;
        HomeDiscordDetail.Text = DiscordSummary();

        HomeInboxBadge.Visibility = _unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeInboxCount.Text = _unread > 9 ? "9+" : _unread.ToString();

        // Nothing going on: the clock.
        var idle = music == null && !timerOn && download == null;
        HomeIdleCard.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        if (idle)
        {
            HomeClock.Text = DateTime.Now.ToString("HH:mm");
            HomeDate.Text = DateTime.Now.ToString("d MMMM, dddd", HomeCulture);
            var stats = Settings.Get(Feature.SystemStats);
            HomeStats.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
            if (stats)
            {
                var sample = _stats.Sample();
                HomeStats.Text = $"CPU %{sample.CpuPercent}  ·  RAM %{sample.RamPercent}"
                                 + (sample.GpuTemperature is { } gpu ? $"  ·  GPU {gpu}°" : "");
            }
        }

        HomeContent.Measure(new Size(HomePanel.Width, double.PositiveInfinity));
        var height = Math.Ceiling(HomeContent.DesiredSize.Height);
        if (Math.Abs(HomePanel.Height - height) > 0.5)
        {
            HomePanel.Height = height;
            Refresh();
        }
    }

    void OpenHome()
    {
        ShowTimerChips(false);
        Expand(ExpandKind.Home);
        RefreshWeatherCard();
        RefreshAppUsage();
        _discordRunning = System.Diagnostics.Process.GetProcessesByName(DiscordName).Length > 0;
        UpdateCalendarCard();
        UpdateHome();
        _homeTimer.Start();
    }

    /// <summary>Opens a card from Home; clicking the card's background comes back here.</summary>
    void OpenFromHome(ExpandKind kind)
    {
        Expand(kind);
        _fromHome = true;
    }

    void ShowTimerChips(bool show)
    {
        HomeTiles.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        HomeChips.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Most used apps ----------

    const int TopAppCount = 5;
    static readonly TimeSpan AppUsageMaxAge = TimeSpan.FromMinutes(10);
    List<AppUsage> _appUsage = [];
    DateTime _appUsageAt;

    void RefreshAppUsage()
    {
        if (DateTime.Now - _appUsageAt < AppUsageMaxAge)
            return;
        _appUsageAt = DateTime.Now;
        _appUsage = AppUsageReader.Read();

        HomeApps.Children.Clear();
        foreach (var app in _appUsage.Take(TopAppCount))
            HomeApps.Children.Add(AppTile(app));
        HomeAppsCard.Visibility = HomeApps.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    Button AppTile(AppUsage app)
    {
        var stack = new StackPanel { Width = 64 };
        stack.Children.Add(new Image { Source = app.Icon, Width = 34, Height = 34, Margin = new Thickness(0, 0, 0, 5) });

        var name = new TextBlock { Text = app.Name, FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        stack.Children.Add(name);

        foreach (var line in new[] { app.FocusText, app.RunsText }.Where(l => l.Length > 0))
        {
            var stat = new TextBlock { Text = line, FontSize = 10, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
            stat.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            stack.Children.Add(stat);
        }

        var tile = new Button { Style = (Style)FindResource("IconButton"), Content = stack, ToolTip = app.Name };
        tile.Click += (_, _) =>
        {
            Expand(ExpandKind.None);
            app.Open();
        };
        return tile;
    }

    // ---------- Live cards ----------

    void HomeMusic_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenFromHome(ExpandKind.Media);
    }

    void HomeTimer_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenFromHome(ExpandKind.Timer);
    }

    void HomeDownload_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenDownloadsFolder();
    }

    // ---------- Shortcuts ----------



    void HomeTimerTile_Click(object sender, RoutedEventArgs e)
    {
        // A running timer opens its card; otherwise pick a length.
        if (_timer is TimerState.Running or TimerState.Paused)
            OpenFromHome(ExpandKind.Timer);
        else
            ShowAndFitChips(true);
    }

    void HomeChipsBack_Click(object sender, RoutedEventArgs e) => ShowAndFitChips(false);

    void ShowAndFitChips(bool show)
    {
        ShowTimerChips(show);
        UpdateHome();
    }

    void HomeClipboard_Click(object sender, RoutedEventArgs e) => OpenFromHome(ExpandKind.Clipboard);

    void HomeShelf_Click(object sender, RoutedEventArgs e) => OpenFromHome(ExpandKind.Shelf);

    void HomeSettings_Click(object sender, RoutedEventArgs e)
    {
        Expand(ExpandKind.None);
        OpenSettings();
    }
}
