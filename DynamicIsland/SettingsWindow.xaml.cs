using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>iOS-style settings: grouped cards of switches, a theme picker and update checking.</summary>
public partial class SettingsWindow : Window
{
    readonly Func<Task> _checkForUpdates;
    TextBlock? _updateStatus;
    Button? _updateButton;

    public SettingsWindow(Func<Task> checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;
        InitializeComponent();
        Theme.Apply(Resources);
        Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => Settings.Changed -= OnSettingsChanged;
        Build();
    }

    void OnSettingsChanged() => Theme.Apply(Resources);

    void Build()
    {
        AddGroup("Genel",
            ToggleRow("Windows ile başlat", "Bilgisayar açılınca ada da açılır", () => Autostart.IsEnabled, v => Autostart.IsEnabled = v),
            ThemeRow());

        AddGroup("Müzik",
            ToggleRow("Müziği sürekli göster", "Kapalıyken sadece şarkı değişince görünür",
                () => Settings.AlwaysShowMedia, v => Settings.AlwaysShowMedia = v),
            FeatureRow(Feature.Marquee, "Kayan şarkı adı", "Sığmayan isimler yavaşça kayar"),
            FeatureRow(Feature.RealWaveform, "Gerçek ses dalgası", "Çubuklar çalan müziğe göre hareket eder"));

        AddGroup("Etkinlikler",
            FeatureRow(Feature.Notifications, "Bildirimler", "Uygulama bildirimleri adada görünür"),
            FeatureRow(Feature.Volume, "Ses seviyesi"),
            FeatureRow(Feature.Battery, "Şarj ve düşük pil"),
            FeatureRow(Feature.LockKeys, "Caps Lock ve Num Lock"),
            FeatureRow(Feature.Screenshots, "Ekran görüntüsü önizlemesi"),
            FeatureRow(Feature.Clipboard, "Pano geçmişi", "Son 5 metin, sadece bellekte tutulur"),
            FeatureRow(Feature.Downloads, "İndirmeler"),
            FeatureRow(Feature.Privacy, "Kamera ve mikrofon", "Bir uygulama kullanmaya başlayınca haber verir"),
            FeatureRow(Feature.Shelf, "Dosya rafı", "Dosyaları adaya sürükleyip bırak"),
            FeatureRow(Feature.GameSummary, "Oyun oturumu özeti", "Oyun kapanınca ne kadar oynadığını gösterir"),
            FeatureRow(Feature.SystemStats, "Sistem durumu", "Üst kenarda beklerken CPU, RAM ve GPU"));

        AddGroup("Güncelleme", UpdateRow());

        VersionText.Text = $"Dynamic Island {Updater.CurrentVersion.ToString(3)}";
    }

    void AddGroup(string caption, params FrameworkElement[] rows)
    {
        Groups.Children.Add(new TextBlock
        {
            Text = caption.ToUpperInvariant(),
            FontSize = 12,
            Foreground = (Brush)FindResource("SecondaryText"),
            Margin = new Thickness(16, 6, 0, 6),
        });

        var list = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var separator = new Border { Height = 0.6, Margin = new Thickness(16, 0, 0, 0) };
                separator.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
                list.Children.Add(separator);
            }
            list.Children.Add(rows[i]);
        }

        var card = new Border { CornerRadius = new CornerRadius(12), Child = list, Margin = new Thickness(0, 0, 0, 22) };
        card.SetResourceReference(Border.BackgroundProperty, "GroupBg");
        Groups.Children.Add(card);
    }

    FrameworkElement FeatureRow(string feature, string title, string? subtitle = null) =>
        ToggleRow(title, subtitle, () => Settings.Get(feature), v => Settings.Set(feature, v));

    FrameworkElement ToggleRow(string title, string? subtitle, Func<bool> get, Action<bool> set)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
        toggle.Click += (_, _) => set(toggle.IsChecked == true);
        return Row(title, subtitle, toggle);
    }

    FrameworkElement ThemeRow()
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

        var frame = new Border { CornerRadius = new CornerRadius(9), Child = segments, VerticalAlignment = VerticalAlignment.Center };
        frame.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        return Row("Tema", null, frame);
    }

    FrameworkElement UpdateRow()
    {
        _updateButton = new Button { Content = "Denetle", Style = (Style)FindResource("PillButton"), VerticalAlignment = VerticalAlignment.Center };
        _updateButton.Click += async (_, _) =>
        {
            _updateButton.IsEnabled = false;
            _updateStatus!.Text = "Denetleniyor…";
            try
            {
                await _checkForUpdates();
            }
            finally
            {
                _updateButton.IsEnabled = true;
            }
        };
        var row = Row("Güncellemeleri denetle", "Yeni sürüm varsa adada haber verilir", _updateButton);
        _updateStatus = (TextBlock)((StackPanel)((Grid)row).Children[0]).Children[1];
        return row;
    }

    /// <summary>Shows the result of an update check under the button.</summary>
    public void SetUpdateStatus(string text)
    {
        if (_updateStatus != null)
            _updateStatus.Text = text;
    }

    Grid Row(string title, string? subtitle, FrameworkElement accessory)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock { Text = title, FontSize = 14.5 };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        text.Children.Add(titleBlock);

        var subtitleBlock = new TextBlock { Text = subtitle ?? "", FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        subtitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        subtitleBlock.Visibility = string.IsNullOrEmpty(subtitle) && accessory is not Button ? Visibility.Collapsed : Visibility.Visible;
        text.Children.Add(subtitleBlock);

        var grid = new Grid { Margin = new Thickness(16, 10, 14, 10), MinHeight = 31 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(accessory, 1);
        accessory.Margin = new Thickness(12, 0, 0, 0);
        grid.Children.Add(accessory);
        return grid;
    }
}
