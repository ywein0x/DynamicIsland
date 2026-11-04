using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

// Discord card and the notification list.
// Discord doesn't tell other apps who is in a channel. "In a call" comes from Discord holding the
// microphone, and "someone is talking" from how loud Discord is playing.
public partial class MainWindow
{
    const string DiscordName = "Discord";
    const string DiscordAppId = "com.squirrel.Discord.Discord";
    const float TalkingPeak = 0.02f;
    const double DiscordLevelWidth = 332;
    const int InboxSize = 30;
    const int InboxShown = 6;
    const int DiscordMessagesShown = 3;

    sealed record InboxItem(ToastInfo Toast, DateTime At);

    readonly DispatcherTimer _discordFlashTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly DispatcherTimer _discordPoll = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly List<InboxItem> _inbox = [];

    bool _discordCall, _discordFlash, _discordTalking, _discordRunning;
    DateTime _callStart;
    TimeSpan _callsTotal;
    int _callCount, _unread, _discordUnread;

    static bool DiscordOn => Settings.Get(Feature.DiscordActivity);

    TimeSpan CallLength => DateTime.Now - _callStart;

    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    void InitDiscord()
    {
        _discordFlashTimer.Tick += (_, _) =>
        {
            _discordFlashTimer.Stop();
            _discordFlash = false;
            Refresh();
        };
        _discordPoll.Tick += (_, _) => PollDiscordAudio();
    }

    void StartDiscord()
    {
        // The real Discord icon everywhere it appears.
        var icon = DiscordIcon();
        foreach (var badge in new[] { HomeDiscordIcon, DiscordPillIcon, DiscordCardIcon })
            SetIcon(badge, icon);

        // Already in a call when the island started.
        if (_privacy.MicInUseBy(DiscordName))
            SetDiscordCall(true, announce: false);
    }

    static ImageSource? DiscordIcon()
    {
        if (ShellIcons.ForApp(DiscordAppId) is { } icon)
            return icon;
        try
        {
            var path = Process.GetProcessesByName(DiscordName).FirstOrDefault()?.MainModule?.FileName;
            return path != null ? ShellIcons.ForParsingName(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Puts a real icon in a colored placeholder badge.</summary>
    static void SetIcon(Border badge, ImageSource? icon)
    {
        if (icon == null)
            return;
        badge.Background = Brushes.Transparent;
        badge.Child = new Image { Source = icon, Stretch = Stretch.Uniform };
    }

    void SetDiscordCall(bool inCall, bool announce = true)
    {
        if (inCall == _discordCall)
            return;
        _discordCall = inCall;
        if (inCall)
        {
            _callStart = DateTime.Now;
            _callCount++;
            _discordFlash = announce;
            if (announce)
            {
                _discordFlashTimer.Stop();
                _discordFlashTimer.Start();
            }
            _discordPoll.Start();
            PollDiscordAudio();
        }
        else
        {
            var length = CallLength;
            _callsTotal += length;
            _discordPoll.Stop();
            _discordFlash = false;
            _discordTalking = false;
            if (announce && DiscordOn)
                ShowTransient(Transient.Privacy, TimeSpan.FromSeconds(3),
                    InfoContent("Sesli kanaldan ayrıldın", Clock(length), dot: Color.FromRgb(0x8E, 0x8E, 0x93)));
        }
        if (_expand == ExpandKind.Discord)
            BuildDiscordCard();
        Refresh();
    }

    void PollDiscordAudio()
    {
        var peak = _volume.SessionPeak(DiscordName);
        _discordTalking = peak > TalkingPeak;
        var state = _discordTalking ? "Konuşuluyor" : "Sessiz";
        var time = Clock(CallLength);

        DiscordDot.Fill = _discordTalking ? new SolidColorBrush(GreenColor) : (Brush)FindResource("TrackBrush");
        DiscordState.Text = $"{state} · {time}";

        DiscordCallTime.Text = time;
        DiscordCallState.Text = state;
        DiscordLevel.Width = DiscordLevelWidth * Math.Clamp(peak * 2.5, 0, 1);
    }

    // ---------- Discord card ----------

    void HomeDiscord_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenFromHome(ExpandKind.Discord);
        _discordUnread = 0;
        BuildDiscordCard();
    }

    void DiscordOpen_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Expand(ExpandKind.None);
        try { AppLauncher.Open(DiscordName); }
        catch { /* Discord not installed */ }
    }

    void BuildDiscordCard()
    {
        DiscordCardStatus.Text = _discordCall ? "Sesli kanalda" : _discordRunning ? "Açık" : "Kapalı";
        DiscordCallRow.Visibility = _discordCall ? Visibility.Visible : Visibility.Collapsed;
        if (_discordCall)
            PollDiscordAudio();

        // Usage: what Windows has recorded, plus calls since the island started.
        var usage = _appUsage.FirstOrDefault(a => a.Name.Contains(DiscordName, StringComparison.OrdinalIgnoreCase));
        var parts = new List<string>();
        if (usage != null)
            parts.AddRange(new[] { usage.FocusText, usage.RunsText }.Where(p => p.Length > 0));
        if (_callCount > 0)
        {
            var total = _callsTotal + (_discordCall ? CallLength : TimeSpan.Zero);
            var length = total.TotalHours >= 1 ? $", {total.TotalHours:0.#} sa" : total.TotalMinutes >= 1 ? $", {(int)total.TotalMinutes} dk" : "";
            parts.Add($"{_callCount} görüşme{length}");
        }
        DiscordUsage.Text = string.Join("  ·  ", parts);
        DiscordUsage.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        DiscordMessages.Children.Clear();
        var messages = _inbox.Where(i => i.Toast.App == DiscordName).Take(DiscordMessagesShown).ToList();
        if (messages.Count == 0)
        {
            var empty = new TextBlock { Text = "Yeni mesaj yok", FontSize = 12.5, Margin = new Thickness(4, 0, 0, 6) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            DiscordMessages.Children.Add(empty);
        }
        foreach (var item in messages)
            DiscordMessages.Children.Add(InboxRow(item));

        DiscordCardContent.Measure(new Size(DiscordCard.Width, double.PositiveInfinity));
        DiscordCard.Height = Math.Ceiling(DiscordCardContent.DesiredSize.Height);
        Refresh();
    }

    /// <summary>The one-line summary on the home menu's Discord row.</summary>
    string DiscordSummary()
    {
        if (_discordCall)
            return (_discordTalking ? "Konuşuluyor" : "Sesli kanalda") + " · " + Clock(CallLength);
        if (_discordUnread > 0 && _inbox.FirstOrDefault(i => i.Toast.App == DiscordName) is { } latest)
            return $"{latest.Toast.Title}: {latest.Toast.Body}";
        return "Açık";
    }

    // ---------- Notification list ----------

    void AddToInbox(ToastInfo toast)
    {
        _inbox.Insert(0, new InboxItem(toast, DateTime.Now));
        if (_inbox.Count > InboxSize)
            _inbox.RemoveAt(_inbox.Count - 1);
        _unread++;
        if (toast.App == DiscordName)
            _discordUnread++;
    }

    void HomeInbox_Click(object sender, RoutedEventArgs e)
    {
        OpenFromHome(ExpandKind.Inbox);
        _unread = 0;
        _discordUnread = 0;
        BuildInbox();
    }

    void BuildInbox()
    {
        InboxQuiet.Content = Settings.DoNotDisturb ? "Rahatsız Etme: Açık" : "Rahatsız Etme: Kapalı";
        InboxList.Children.Clear();
        if (_inbox.Count == 0)
        {
            var empty = new TextBlock { Text = "Yeni bildirim yok", FontSize = 12.5, Margin = new Thickness(2, 4, 0, 8) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            InboxList.Children.Add(empty);
        }
        foreach (var item in _inbox.Take(InboxShown))
            InboxList.Children.Add(InboxRow(item));

        InboxContent.Measure(new Size(InboxPanel.Width, double.PositiveInfinity));
        InboxPanel.Height = Math.Ceiling(InboxContent.DesiredSize.Height);
        Refresh();
    }

    FrameworkElement InboxRow(InboxItem item)
    {
        var toast = item.Toast;
        var icon = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(NotificationService.BrandColor(toast.App)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = toast.App[..1].ToUpperInvariant(),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        // The picture the notification came with (e.g. an avatar), or the app's real icon.
        if (toast.Icon != null)
        {
            icon.Child = null;
            icon.Background = new ImageBrush(toast.Icon) { Stretch = Stretch.UniformToFill };
        }
        else
        {
            SetIcon(icon, ShellIcons.ForApp(toast.AppId));
        }

        var head = new TextBlock { Text = $"{toast.App} · {toast.Title}", FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        var body = new TextBlock { Text = toast.Body, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        body.Visibility = toast.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        text.Children.Add(head);
        text.Children.Add(body);

        var ago = DateTime.Now - item.At;
        var when = new TextBlock
        {
            Text = ago.TotalMinutes < 1 ? "şimdi" : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes} dk" : item.At.ToString("HH:mm"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        when.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(when, 2);
        grid.Children.Add(when);

        var row = new Border { Style = (Style)FindResource("HomeCard"), Padding = new Thickness(9, 7, 11, 7), Margin = new Thickness(0, 0, 0, 5), Cursor = Cursors.Hand, Child = grid };
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Expand(ExpandKind.None);
            try { Process.Start("explorer.exe", $"shell:AppsFolder\\{toast.AppId}"); }
            catch { /* app can't be launched this way */ }
        };
        return row;
    }

    void InboxQuiet_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Settings.DoNotDisturb = !Settings.DoNotDisturb;
        BuildInbox();
    }

    void InboxClear_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _inbox.Clear();
        BuildInbox();
    }
}
