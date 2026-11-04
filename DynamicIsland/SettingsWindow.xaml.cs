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

    FrameworkElement[] GeneralPage() =>
    [
        Group(null,
            ToggleRow("Windows ile başlat", "Bilgisayar açılınca ada da açılır", () => Autostart.IsEnabled, v => Autostart.IsEnabled = v),
            Row("Şehir", "Ana menüdeki hava durumu kartı için", CityBox())),
        Note("Adayı kapatmak için gizli simgeler alanındaki ikona sağ tıklayıp Çıkış'ı seç."),
    ];

    FrameworkElement[] AppearancePage() =>
    [
        Group(null,
            ThemeRow(),
            SegmentRow("Ada boyutu", [("small", "Küçük"), ("normal", "Normal"), ("large", "Büyük")],
                Settings.IslandSize, v => Settings.IslandSize = v),
            SegmentRow("Köşeler", [("sharp", "Keskin"), ("normal", "Normal"), ("round", "Yuvarlak")],
                Settings.IslandCorners, v => Settings.IslandCorners = v),
            Row("Vurgu rengi", "Hava durumu, takvim, ses ve müzik çubukları", AccentPicker())),
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
    }    FrameworkElement[] MusicPage() =>
    [
        Group(null,
            ToggleRow("Müziği sürekli göster", "Kapalıyken sadece şarkı değişince birkaç saniye görünür",
                () => Settings.AlwaysShowMedia, v => Settings.AlwaysShowMedia = v),
            FeatureRow(Feature.Marquee, "Kayan şarkı adı", "Sığmayan isimler yavaşça kayar"),
            FeatureRow(Feature.RealWaveform, "Gerçek ses dalgası", "Çubuklar çalan müziğe göre hareket eder")),
        Note("YouTube videoları ve Shorts gösterilmez; sadece müzik uygulamaları ve YouTube Music gibi şarkı bilgisi gönderen siteler."),
    ];

    FrameworkElement[] NotificationsPage()
    {
        var page = new List<FrameworkElement>
        {
            Group(null,
                FeatureRow(Feature.Notifications, "Uygulama bildirimleri", "WhatsApp, Discord gibi uygulamaların bildirimleri"),
                ToggleRow("Rahatsız Etme", "Bildirimler adada açılmaz, ana menüdeki Bildirim listesinde birikir",
                    () => Settings.DoNotDisturb, v => Settings.DoNotDisturb = v),
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
            FeatureRow(Feature.LockKeys, "Caps Lock ve Num Lock"),
            FeatureRow(Feature.Privacy, "Kamera ve mikrofon", "Bir uygulama kullanmaya başlayınca haber verir"),
            FeatureRow(Feature.DiscordActivity, "Discord", "Sesli kanalda olduğunu ve konuşulduğunu gösterir"),
            FeatureRow(Feature.SystemStats, "Sistem durumu", "Üst kenarda beklerken CPU, RAM ve GPU sıcaklığı")),
    ];

    FrameworkElement[] ToolsPage() =>
    [
        Group(null,
            FeatureRow(Feature.Clipboard, "Pano geçmişi", "Son 5 kopyalanan metin, sadece bellekte tutulur"),
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
