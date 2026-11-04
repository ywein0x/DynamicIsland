using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        var showWeather = Settings.Get(Feature.HomeWeather);
        var showCalendar = Settings.Get(Feature.HomeCalendar);
        HomeWidgets.Visibility = showWeather || showCalendar ? Visibility.Visible : Visibility.Collapsed;
        HomeWeatherCard.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
        HomeCalendarCard.Visibility = showCalendar ? Visibility.Visible : Visibility.Collapsed;
        // One card alone takes the whole row.
        Grid.SetColumn(HomeCalendarCard, showWeather ? 2 : 0);
        Grid.SetColumnSpan(HomeCalendarCard, showWeather ? 1 : 3);
        Grid.SetColumnSpan(HomeWeatherCard, showCalendar ? 1 : 3);

        HomeAppsCard.Visibility = Settings.Get(Feature.HomeApps) && HomeApps.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        TileTimer.Visibility = Settings.Get(Feature.TileTimer) ? Visibility.Visible : Visibility.Collapsed;
        TileInbox.Visibility = Settings.Get(Feature.TileInbox) ? Visibility.Visible : Visibility.Collapsed;
        TileFavorites.Visibility = Settings.Get(Feature.TileFavorites) ? Visibility.Visible : Visibility.Collapsed;
        TileClipboard.Visibility = Settings.Get(Feature.TileClipboard) ? Visibility.Visible : Visibility.Collapsed;
        TileShelf.Visibility = Settings.Get(Feature.TileShelf) ? Visibility.Visible : Visibility.Collapsed;
        HomeTiles.Columns = HomeTiles.Children.OfType<UIElement>().Count(c => c.Visibility == Visibility.Visible);

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
        _ = CheckDiscordRunningAsync();
        UpdateCalendarCard();
        UpdateHome();
        _homeTimer.Start();
    }

    /// <summary>Opens a card from Home; clicking the card's background comes back here.</summary>
    void OpenFromHome(ExpandKind kind)
    {
        Expand(kind);
        _fromHome = true;
        _reachPageUntil = DateTime.Now + ReachPageTime;
        _collapseTimer.Interval = ReachPageTime;
        _collapseTimer.Start();
    }

    void ShowTimerChips(bool show)
    {
        HomeTiles.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        HomeChips.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Most used apps ----------


    static readonly TimeSpan AppUsageMaxAge = TimeSpan.FromMinutes(10);
    List<AppUsage> _appUsage = [];
    DateTime _appUsageAt;

    /// <summary>Starts a background reading when the last one is old; the menu never waits for it.</summary>
    void RefreshAppUsage()
    {
        if (DateTime.Now - _appUsageAt < AppUsageMaxAge)
            return;
        _appUsageAt = DateTime.Now;
        _ = LoadAppUsageAsync();
    }

    async Task LoadAppUsageAsync()
    {
        _appUsage = await AppUsageReader.ReadInBackgroundAsync();
        BuildAppTiles();
    }

    void BuildAppTiles()
    {
        var hideBrowsers = Settings.Get(Feature.HideBrowsers);
        var apps = _appUsage
            .Where(a => !(hideBrowsers && AppUsageReader.IsBrowser(a)) && !Settings.IsAppHidden(a.Name))
            .Take(int.Parse(Settings.TopAppCount));

        HomeApps.Children.Clear();
        foreach (var app in apps)
            HomeApps.Children.Add(AppTile(app));
        HomeApps.Columns = Math.Max(1, HomeApps.Children.Count);
        UpdateHome();
    }

    async Task CheckDiscordRunningAsync()
    {
        _discordRunning = await Task.Run(() => System.Diagnostics.Process.GetProcessesByName(DiscordName).Length > 0);
        UpdateHome();
    }

    FrameworkElement AppTile(AppUsage app)
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

        // Shows on hover; Settings > Ana menü brings the app back.
        var remove = new Button
        {
            Style = (Style)FindResource("IconButton"),
            ToolTip = "Listeden kaldır",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -3, 3, 0),
            Visibility = Visibility.Hidden,
            Content = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(8.5),
                Background = new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x66)),
                Child = new TextBlock
                {
                    Text = "",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 8,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            Settings.SetAppHidden(app.Name, true);
        };

        var holder = new Grid { Background = Brushes.Transparent, Tag = app.Name };
        holder.Children.Add(tile);
        holder.Children.Add(remove);
        holder.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        holder.MouseLeave += (_, _) => remove.Visibility = Visibility.Hidden;
        return holder;
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

    /// <summary>"Ana menü" link at the top of every page.</summary>
    void Back_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OpenHome();
    }

    void HomeClipboard_Click(object sender, RoutedEventArgs e) => OpenFromHome(ExpandKind.Clipboard);

    void HomeShelf_Click(object sender, RoutedEventArgs e) => OpenFromHome(ExpandKind.Shelf);

    void HomeSettings_Click(object sender, RoutedEventArgs e)
    {
        Expand(ExpandKind.None);
        OpenSettings();
    }
}
