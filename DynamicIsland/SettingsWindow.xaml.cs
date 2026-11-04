using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;


namespace DynamicIsland;

/// <summary>
/// Settings in the style of macOS System Settings: categories with colored icons on the left,
/// grouped cards of rows on the right.
/// </summary>
public partial class SettingsWindow : Window
{
    sealed record Category(string Title, string Glyph, string Color, Func<FrameworkElement[]> Build);

    readonly Func<Task> _checkForUpdates;
    readonly Category[] _categories;
    string _updateStatusText = "";
    TextBlock? _updateStatus;


    public SettingsWindow(Func<Task> checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;
        InitializeComponent();
        Theme.Apply(Resources);

        _categories =
        [
            new("Genel", "", "#8E8E93", GeneralPage),
            new("Görünüm", "", "#AF52DE", AppearancePage),
            new("Ana menü", "", "#FF9500", HomePage),
            new("Müzik", "", "#FF2D55", MusicPage),
            new("Bildirimler", "", "#FF3B30", NotificationsPage),
            new("Sistem", "", "#34C759", SystemPage),
            new("Araçlar", "", "#FF9500", ToolsPage),
            new("Güncelleme", "", "#0A84FF", UpdatePage),
        ];

        var version = $"Sürüm {Updater.CurrentVersion.ToString(3)}";
        SidebarVersion.Text = version;
        AppIcon.Source = LoadAppIcon();
        BuildNav();

        Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => Settings.Changed -= OnSettingsChanged;
        SourceInitialized += (_, _) => Theme.ApplyWindowFrame(new WindowInteropHelper(this).Handle);
    }

    void OnSettingsChanged()
    {
        Theme.Apply(Resources);
        Theme.ApplyWindowFrame(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Shows the result of an update check on the Güncelleme page.</summary>
    public void SetUpdateStatus(string text)
    {
        _updateStatusText = text;
        if (_updateStatus != null)
            _updateStatus.Text = text;
    }

    // ---------- Navigation ----------

    void BuildNav()
    {
        foreach (var category in _categories)
        {
            var icon = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(5.5),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(category.Color)),
                Child = new TextBlock
                {
                    Text = category.Glyph,
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 12,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var label = new TextBlock { Text = category.Title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(label);

            var item = new RadioButton { Style = (Style)FindResource("NavItem"), GroupName = "Nav", Content = content };
            item.Checked += (_, _) => ShowPage(category);
            Nav.Children.Add(item);
        }
        ((RadioButton)Nav.Children[0]).IsChecked = true;
    }

    void ShowPage(Category category)
    {
        _updateStatus = null;

        Page.Children.Clear();
        Page.Children.Add(new TextBlock
        {
            Text = category.Title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 18),
        }.Themed(TextBlock.ForegroundProperty, "Fg"));

        foreach (var element in category.Build())
            Page.Children.Add(element);

        PageScroll.ScrollToTop();
        // A quick fade so switching pages feels smooth.
        Page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    // ---------- Pages ----------

    FrameworkElement[] GeneralPage()
    {
        var reset = new Button { Content = "Sıfırla", Style = (Style)FindResource("PillButton") };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Tüm ayarlar varsayılana dönsün mü? Beğeniler ve takvim etkinlikleri silinmez.", "Dynamic Island",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            Settings.ResetAll();
            ((RadioButton)Nav.Children[0]).IsChecked = false;
            ((RadioButton)Nav.Children[0]).IsChecked = true;
        };

        return
        [
            Group(null,
                ToggleRow("Windows ile başlat", "Bilgisayar açılınca ada da açılır", () => Autostart.IsEnabled, v => Autostart.IsEnabled = v),
                Row("Şehir", "Ana menüdeki hava durumu için", CityBox()),
                SegmentRow("Üst kenardan açılma", [("fast", "Hızlı"), ("normal", "Normal"), ("slow", "Yavaş"), ("off", "Kapalı")],
                    Settings.RevealDelay, v => Settings.RevealDelay = v)),
            Note("Üst kenardan açılma: fareyi ekranın en üst ortasında ne kadar bekletince adanın görüneceği. "
                 + "Kapalıysa ana menüyü gizli simgelerdeki ikona sağ tıklayıp açabilirsin."),
            Group(null, Row("Tüm ayarları sıfırla", "Beğeniler ve takvim etkinlikleri silinmez", reset)),
            Note("Adayı kapatmak için gizli simgeler alanındaki ikona sağ tıklayıp Çıkış'ı seç."),
        ];
    }

    FrameworkElement[] AppearancePage() =>
    [
        Group(null,
            ThemeRow(),
            Row("Vurgu rengi", "Hava durumu, takvim, ses ve müzik çubukları", AccentPicker())),
        Caption("Ada"),
        Group(null,
            SegmentRow("Boyut", [("small", "Küçük"), ("normal", "Normal"), ("large", "Büyük")],
                Settings.IslandSize, v => Settings.IslandSize = v),
            SegmentRow("Köşeler", [("sharp", "Keskin"), ("normal", "Normal"), ("round", "Yuvarlak")],
                Settings.IslandCorners, v => Settings.IslandCorners = v),
            SegmentRow("Üstten boşluk", [("0", "Yok"), ("8", "Az"), ("16", "Çok")],
                Settings.IslandGap, v => Settings.IslandGap = v)),
        Caption("Hareket"),
        Group(null,
            SegmentRow("Animasyon", [("calm", "Sakin"), ("normal", "Normal"), ("fast", "Hızlı")],
                Settings.AnimationSpeed, v => Settings.AnimationSpeed = v),
            FeatureRow(Feature.SoftEntrance, "Yumuşak geçiş", "Küçük haplar bulanıktan netleşerek belirir"),
            FeatureRow(Feature.Shadow, "Gölge", "Açık kartların altındaki gölge")),
    ];

    Grid SegmentRow(string title, (string Value, string Label)[] options, string current, Action<string> set)
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (value, label) in options)
        {
            var option = new RadioButton { Content = label, GroupName = title, Style = (Style)FindResource("Segment"), IsChecked = current == value };
            option.Checked += (_, _) => set(value);
            segments.Children.Add(option);
        }
        var frame = new Border { CornerRadius = new CornerRadius(8), Child = segments }.Themed(Border.BackgroundProperty, "TrackBrush");
        return Row(title, null, frame);
    }

    FrameworkElement AccentPicker()
    {
        string[] colors = [Theme.DefaultAccent, "#30D158", "#FF9F0A", "#FF375F", "#BF5AF2", "#FFD60A"];
        var picker = new StackPanel { Orientation = Orientation.Horizontal };
        var swatches = new List<(Border Swatch, string Color)>();
        void Mark()
        {
            foreach (var (swatch, color) in swatches)
                swatch.BorderThickness = new Thickness(string.Equals(color, Settings.Accent, StringComparison.OrdinalIgnoreCase) ? 2.5 : 0);
        }
        foreach (var color in colors)
        {
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(5, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "Fg");
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                Settings.Accent = color;
                Mark();
            };
            swatches.Add((swatch, color));
            picker.Children.Add(swatch);
        }
        Mark();
        return picker;
    }

    TextBlock Caption(string text) =>
        new TextBlock { Text = text.ToUpper(new System.Globalization.CultureInfo("tr-TR")), FontSize = 11.5, Margin = new Thickness(14, 6, 0, 6) }
            .Themed(TextBlock.ForegroundProperty, "SecondaryText");

    /// <summary>The city for the weather card; saved when the field loses focus.</summary>
    FrameworkElement CityBox()
    {
        var box = new TextBox
        {
            Width = 160,
            FontSize = 12.5,
            Text = Settings.WeatherCity,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(Control.ForegroundProperty, "Fg");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Fg");
        box.LostFocus += (_, _) =>
        {
            if (box.Text.Trim().Length > 0)
                Settings.WeatherCity = box.Text.Trim();
        };
        return new Border { CornerRadius = new CornerRadius(7), Child = box }.Themed(Border.BackgroundProperty, "TrackBrush");
    }

    FrameworkElement[] HomePage()
    {
        var page = new List<FrameworkElement>
        {
            Caption("Kartlar"),
            Group(null,
                FeatureRow(Feature.HomeWeather, "Hava durumu"),
                FeatureRow(Feature.HomeCalendar, "Takvim"),
                FeatureRow(Feature.HomeApps, "En çok kullanılanlar"),
                FeatureRow(Feature.SystemStats, "Sistem durumu", "Boştayken CPU, RAM ve GPU sıcaklığı")),
            Caption("Kısayollar"),
            Group(null,
                FeatureRow(Feature.TileTimer, "Zamanlayıcı"),
                FeatureRow(Feature.TileInbox, "Bildirimler"),
                FeatureRow(Feature.TileFavorites, "Beğeniler"),
                FeatureRow(Feature.TileClipboard, "Pano"),
                FeatureRow(Feature.TileShelf, "Dosya rafı")),
            Caption("En çok kullanılanlar"),
            Group(null,
                SegmentRow("Kaç uygulama", [("3", "3"), ("4", "4"), ("5", "5")], Settings.TopAppCount, v => Settings.TopAppCount = v),
                FeatureRow(Feature.HideBrowsers, "Tarayıcıları gizle", "Chrome, Edge, Opera, Brave ve benzerleri listede yer almaz")),
        };

        // One switch per app, so anything can be taken off the list.
        var hideBrowsers = Settings.Get(Feature.HideBrowsers);
        var apps = AppUsageReader.Latest.Where(a => !(hideBrowsers && AppUsageReader.IsBrowser(a)))
            .Where((a, i) => i < 10 || Settings.IsAppHidden(a.Name)).ToList();
        if (apps.Count > 0)
        {
            page.Add(Caption("Listede gösterilecek uygulamalar"));
            page.Add(Group(null, apps.Select(app => (FrameworkElement)ToggleRow(app.Name,
                string.Join(" · ", new[] { app.FocusText, app.RunsText }.Where(s => s.Length > 0)),
                () => !Settings.IsAppHidden(app.Name), v => Settings.SetAppHidden(app.Name, !v))).ToArray()));
        }
        page.Add(Note("Sayılar Windows'un kendi kaydından gelir, ada ayrıca takip yapmaz. Windows bazı açılışları saymaz, bu yüzden gerçek rakamlar daha yüksek olabilir."));
        return [.. page];
    }

    FrameworkElement[] MusicPage() =>
    [
        Group(null,
            ToggleRow("Müziği sürekli göster", "Kapalıyken sadece şarkı değişince görünür",
                () => Settings.AlwaysShowMedia, v => Settings.AlwaysShowMedia = v),
            FeatureRow(Feature.MusicFlash, "Şarkı değişince göster"),
            SegmentRow("Görünme süresi", [("2", "2 sn"), ("4", "4 sn"), ("8", "8 sn")],
                Settings.MusicFlashSeconds, v => Settings.MusicFlashSeconds = v)),
        Group(null,
            FeatureRow(Feature.Marquee, "Kayan şarkı adı", "Sığmayan isimler yavaşça kayar"),
            FeatureRow(Feature.RealWaveform, "Gerçek ses dalgası", "Çubuklar çalan müziğe göre hareket eder"),
            ToggleRow("Videoları da göster", "YouTube videoları ve Shorts da adada görünür",
                () => Settings.Get(Feature.ShowVideos, fallback: false), v => Settings.Set(Feature.ShowVideos, v))),
        Caption("Spotify"),
        .. SpotifySection(),
    ];

    FrameworkElement[] SpotifySection()
    {
        if (SpotifyClient.Connected)
        {
            var leave = new Button { Content = "Bağlantıyı kes", Style = (Style)FindResource("PillButton") };
            leave.Click += (_, _) =>
            {
                SpotifyClient.Disconnect();
                ReloadPage();
            };
            return
            [
                Group(null, Row("Spotify'a bağlı", "Kalp, Spotify'daki Beğenilenler ile birlikte çalışır", leave)),
                Note("Beğenilmiş şarkılarda kalp dolu görünür; kalbe basınca Spotify'da eklenir veya çıkarılır."),
            ];
        }

        var box = new TextBox
        {
            Width = 190,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(Control.ForegroundProperty, "Fg");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Fg");
        var boxFrame = new Border { CornerRadius = new CornerRadius(7), Child = box, Margin = new Thickness(0, 0, 8, 0) }
            .Themed(Border.BackgroundProperty, "TrackBrush");

        var connect = new Button { Content = "Bağlan", Style = (Style)FindResource("PillButton") };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(boxFrame);
        controls.Children.Add(connect);

        var row = Row("Client ID", "Bağlanmadan kalp sadece adadaki listeye ekler", controls);
        var status = (TextBlock)((StackPanel)row.Children[0]).Children[1];
        connect.Click += async (_, _) =>
        {
            var id = box.Text.Trim();
            if (id.Length == 0)
            {
                status.Text = "Önce Client ID'yi yapıştır";
                return;
            }
            connect.IsEnabled = false;
            status.Text = "Tarayıcıda Spotify'ı onayla…";
            try
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                await SpotifyClient.ConnectAsync(id, wait.Token);
                ReloadPage();
            }
            catch (Exception ex)
            {
                status.Text = ex is OperationCanceledException ? "Bağlantı onaylanmadı" : "Bağlanılamadı, Client ID ve yönlendirme adresini kontrol et";
                connect.IsEnabled = true;
            }
        };

        var open = new Button { Content = "Aç", Style = (Style)FindResource("PillButton") };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(SpotifyClient.DashboardUrl) { UseShellExecute = true });
        var copy = new Button { Content = "Kopyala", Style = (Style)FindResource("PillButton") };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(SpotifyClient.RedirectUri); }
            catch { /* another app is holding the clipboard */ }
        };

        return
        [
            Group(null,
                Row("1. Geliştirici sayfası", "Spotify hesabınla gir, Create app'e bas", open),
                Row("2. Redirect URI", SpotifyClient.RedirectUri + " (Web API'yi de seç)", copy),
                row),
            Note("Spotify'ın kuralı gereği her kullanıcı kendi uygulamasını oluşturur ve hesabın Premium olması gerekir. Bir kez yapman yeterli; "
                 + "giriş bilgin sadece bu bilgisayarda şifreli olarak tutulur. Client ID, oluşturduğun uygulamanın ayarlarında yazar."),
        ];
    }

    void ReloadPage()
    {
        var current = Nav.Children.OfType<RadioButton>().First(r => r.IsChecked == true);
        current.IsChecked = false;
        current.IsChecked = true;
    }

    FrameworkElement[] NotificationsPage()
    {
        var page = new List<FrameworkElement>
        {
            Group(null,
                FeatureRow(Feature.Notifications, "Uygulama bildirimleri", "WhatsApp, Discord gibi uygulamaların bildirimleri"),
                SegmentRow("Görünme süresi", [("3", "3 sn"), ("5", "5 sn"), ("8", "8 sn")],
                    Settings.BannerSeconds, v => Settings.BannerSeconds = v),
                ToggleRow("İçeriği gizle", "Adada sadece \"Yeni bildirim\" yazar; mesaj ana menüdeki listede okunur",
                    () => Settings.Get(Feature.HideNotificationText, fallback: false), v => Settings.Set(Feature.HideNotificationText, v))),
            Caption("Rahatsız Etme"),
            Group(null,
                ToggleRow("Rahatsız Etme", "Bildirimler adada açılmaz, listede birikir",
                    () => Settings.DoNotDisturb, v => Settings.DoNotDisturb = v),
                SegmentRow("Otomatik saatler", [("off", "Kapalı"), ("22-08", "22:00-08:00"), ("00-08", "00:00-08:00")],
                    Settings.QuietHours, v => Settings.QuietHours = v)),
            Caption("Diğer"),
            Group(null,
                FeatureRow(Feature.Downloads, "İndirmeler", "Tarayıcı indirmelerinin ilerlemesi"),
                FeatureRow(Feature.Screenshots, "Ekran görüntüsü önizlemesi", "Win+Shift+S sonrası küçük önizleme")),
        };

        // One switch per app that has sent something so far.
        var apps = Settings.NotificationApps;
        page.Add(Caption("Uygulamalar"));
        if (apps.Count == 0)
        {
            page.Add(Note("Bildirim gönderen uygulamalar burada listelenir; her birini ayrı ayrı kapatabilirsin."));
        }
        else
        {
            page.Add(Group(null, apps.OrderBy(a => a).Select(app =>
                (FrameworkElement)ToggleRow(app, null, () => !Settings.IsAppMuted(app), v => Settings.SetAppMuted(app, !v))).ToArray()));
        }
        return [.. page];
    }

    FrameworkElement[] SystemPage() =>
    [
        Group(null,
            FeatureRow(Feature.Volume, "Ses seviyesi"),
            FeatureRow(Feature.Battery, "Şarj ve düşük pil"),
            FeatureRow(Feature.BatteryBubble, "Pil yuvarlağı", "Ada açılınca sağında pil durumu görünür (pili olan bilgisayarlarda)"),
            FeatureRow(Feature.UsageWarning, "Yüksek kullanım uyarısı", "RAM, ekran kartı veya işlemci çok yüklenince ada açıldığında sağında gösterir"),
            FeatureRow(Feature.LockKeys, "Caps Lock ve Num Lock"),
            FeatureRow(Feature.Privacy, "Kamera ve mikrofon", "Bir uygulama kullanmaya başlayınca haber verir"),
            FeatureRow(Feature.DiscordActivity, "Discord", "Sesli kanalda olduğunu ve konuşulduğunu gösterir")),
    ];

    FrameworkElement[] ToolsPage() =>
    [
        Caption("Zamanlayıcı"),
        Group(null,
            FeatureRow(Feature.TimerSound, "Alarm sesi", "Süre dolunca Windows alarm sesi çalar")),
        Caption("Pano"),
        Group(null,
            FeatureRow(Feature.Clipboard, "Pano geçmişi", "Sadece bellekte tutulur, uygulama kapanınca silinir"),
            SegmentRow("Hatırlanacak metin", [("3", "3"), ("5", "5"), ("8", "8")], Settings.ClipboardSize, v => Settings.ClipboardSize = v)),
        Caption("Diğer"),
        Group(null,
            FeatureRow(Feature.Shelf, "Dosya rafı", "Dosyaları ekranın üst ortasına sürükleyip bırak"),
            FeatureRow(Feature.GameSummary, "Oyun oturumu özeti", "Oyun kapanınca ne kadar oynadığını gösterir")),
    ];

    FrameworkElement[] UpdatePage()
    {
        var button = new Button { Content = "Güncellemeleri denetle", Style = (Style)FindResource("PillButton") };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            SetUpdateStatus("Denetleniyor…");
            try { await _checkForUpdates(); }
            finally { button.IsEnabled = true; }
        };

        var github = new Button { Content = "GitHub sayfası", Style = (Style)FindResource("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
        github.Click += (_, _) => Process.Start(new ProcessStartInfo($"https://github.com/{Updater.Repository}") { UseShellExecute = true });

        var info = Row($"Dynamic Island {Updater.CurrentVersion.ToString(3)}",
            "Yeni sürümler açılışta ve her 6 saatte bir otomatik denetlenir", button);
        _updateStatus = (TextBlock)((StackPanel)info.Children[0]).Children[1];
        if (_updateStatusText.Length > 0)
            _updateStatus.Text = _updateStatusText;

        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 10, 0, 0) };
        links.Children.Add(github);
        return [Group(null, info), links];
    }

    // ---------- Building blocks ----------

    Border Group(string? caption, params FrameworkElement[] rows)
    {
        var list = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
                list.Children.Add(new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) }.Themed(Border.BackgroundProperty, "SeparatorBrush"));
            list.Children.Add(rows[i]);
        }
        return new Border { CornerRadius = new CornerRadius(10), Child = list, Margin = new Thickness(0, 0, 0, 10) }
            .Themed(Border.BackgroundProperty, "GroupBg");
    }

    TextBlock Note(string text) =>
        new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 2, 14, 18) }
            .Themed(TextBlock.ForegroundProperty, "SecondaryText");

    Grid FeatureRow(string feature, string title, string? subtitle = null) =>
        ToggleRow(title, subtitle, () => Settings.Get(feature), v => Settings.Set(feature, v));

    Grid ToggleRow(string title, string? subtitle, Func<bool> get, Action<bool> set)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = get() };
        toggle.Click += (_, _) => set(toggle.IsChecked == true);
        return Row(title, subtitle, toggle);
    }

    Grid ThemeRow()
    {
        var segments = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (value, label) in new[] { ("dark", "Koyu"), ("light", "Açık"), ("system", "Sistem") })
        {
            var option = new RadioButton
            {
                Content = label,
                GroupName = "Theme",
                Style = (Style)FindResource("Segment"),
                IsChecked = Settings.Theme == value,
            };
            option.Checked += (_, _) => Settings.Theme = value;
            segments.Children.Add(option);
        }
        var frame = new Border { CornerRadius = new CornerRadius(8), Child = segments }.Themed(Border.BackgroundProperty, "TrackBrush");
        return Row("Görünüm", null, frame);
    }

    static Grid Row(string title, string? subtitle, FrameworkElement accessory)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13 }.Themed(TextBlock.ForegroundProperty, "Fg"));
        text.Children.Add(new TextBlock
        {
            Text = subtitle ?? "",
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = subtitle == null ? Visibility.Collapsed : Visibility.Visible,
        }.Themed(TextBlock.ForegroundProperty, "SecondaryText"));

        var grid = new Grid { Margin = new Thickness(14, 9, 12, 9), MinHeight = 26 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        accessory.VerticalAlignment = VerticalAlignment.Center;
        accessory.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(accessory, 1);
        grid.Children.Add(accessory);
        return grid;
    }

    static ImageSource? LoadAppIcon()
    {
        try
        {
            var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/island.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}

static class ThemedExtensions
{
    /// <summary>Binds a brush property to a theme resource so it follows light/dark changes.</summary>
    public static T Themed<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
