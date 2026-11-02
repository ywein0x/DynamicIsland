using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland;

// Core: what the island shows, its spring animation, revealing it, the event queue and input.
// Features live in the other MainWindow.*.cs partial files.
public partial class MainWindow : Window
{
    enum Transient { None, Volume, Battery, Notification, Lock, Screenshot, DownloadDone, Privacy, Game, Update }
    enum ExpandKind { None, Media, Timer, Clipboard, Shelf }
    enum BubbleKind { None, Media, Timer, Download }

    sealed record PendingTransient(Transient Kind, TimeSpan Duration, Action Apply, DateTime Since);

    // Island sizes, modeled on the iPhone 15 Pro proportions.
    const double PillHeight = 37;
    const double PillRadius = PillHeight / 2;
    const double BubbleGap = 6;

    // Top-edge reveal: the pointer has to rest in this strip, without clicking, before the island peeks out.
    const double PeekZoneHalfWidth = 90;
    static readonly TimeSpan PeekDwell = TimeSpan.FromMilliseconds(600);

    // Swipe sideways farther than this to dismiss.
    const double SwipeDismissDistance = 70;

    readonly Spring _width = new(126), _height = new(PillHeight), _radius = new(PillRadius), _shadow = new(0), _visible = new(0);
    readonly Spring _shiftX = new(0) { Stiffness = 380, Damping = 34 };
    readonly Spring _bubble = new(0);
    readonly Spring[] _bars = Enumerable.Range(0, 4).Select(_ => new Spring(0.25) { Stiffness = 260, Damping = 26 }).ToArray();
    readonly Rectangle[] _compactBars, _expandedBars;
    readonly FrameworkElement[] _panels;

    readonly DispatcherTimer _transientTimer = new();
    readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly DispatcherTimer _peekHideTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    readonly List<PendingTransient> _pending = [];
    readonly SystemStats _stats = new();
    readonly AudioVisualizer _visualizer = new();

    Transient _transient;
    Action? _transientApply;
    DateTime _transientEnds;
    ExpandKind _expand;
    BubbleKind _bubbleKind;
    bool _hover, _looping;
    bool _hoverSuppressed;  // swiped away while the pointer is still on it
    bool _peek;             // revealed by resting the pointer on the top edge
    bool _peekSuppressed;   // the user clicked in the strip (e.g. a browser tab): don't peek until they leave it
    bool _inPeekZone;
    bool _fullscreen;       // a game or fullscreen video is in front: stay completely out of the way
    DateTime _fullscreenSince;
    string? _fullscreenApp;
    bool _pressing, _swiping;
    Point _pressPoint;
    TimeSpan _dwell;
    FrameworkElement? _panel;
    TimeSpan _lastFrame;
    TrayIcon? _tray;
    SettingsWindow? _settingsWindow;
    UpdateRelease? _update;

    bool Expanded => _expand != ExpandKind.None;

    /// <summary>Pills are skipped while a card is open or a game/fullscreen video is in front.</summary>
    bool Quiet => Expanded || _fullscreen;

    public MainWindow()
    {
        Marquee.Enabled = Settings.Get(Feature.Marquee);
        InitializeComponent();
        Theme.Apply(Resources);

        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;

        _panels =
        [
            CompactMedia, ExpandedMedia, VolumePanel, BatteryPanel, LockPanel, InfoPanel, StatsPanel, TimerCompact, TimerExpanded,
            NotificationPanel, ScreenshotPanel, ClipboardPanel, DownloadPanel, ShelfCompact, ShelfPanel,
        ];
        foreach (var panel in _panels)
        {
            panel.RenderTransformOrigin = new Point(0.5, 0.5);
            panel.RenderTransform = new ScaleTransform();
        }

        _compactBars = CreateBars(CompactBars, width: 3, gap: 2.5);
        _expandedBars = CreateBars(ExpandedBars, width: 3.5, gap: 3);

        _transientTimer.Tick += (_, _) => EndTransient();
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!Island.IsMouseOver && !_dragInside)
                Expand(ExpandKind.None);
        };
        _peekHideTimer.Tick += (_, _) =>
        {
            _peekHideTimer.Stop();
            if (Island.IsMouseOver || _inPeekZone)
                return;
            _peek = false;
            Refresh();
        };
        _cursorTimer.Tick += (_, _) => PollCursor();
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _statsTimer.Tick += (_, _) => ShowStats();
        _updateTimer.Tick += async (_, _) =>
        {
            // First check shortly after startup, then every six hours.
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdatesAsync(manual: false);
        };

        Settings.Changed += OnSettingsChanged;
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.InvokeAsync(() => Theme.Apply(Resources));

        InitMedia();
        InitTimer();
        InitSystem();
        InitShelf();

        Loaded += OnLoaded;
        Apply();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _cursorTimer.Start();
        _fullscreenTimer.Start();
        _updateTimer.Start();
        StartMedia();
        StartSystem();
        _tray = new TrayIcon(
            OpenSettings,
            () => Expand(ExpandKind.Clipboard),
            () => Expand(ExpandKind.Shelf),
            () => _ = CheckForUpdatesAsync(manual: true),
            Close);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Keep the island out of Alt+Tab and never steal focus from the app being used.
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        StartClipboardListener(hwnd);
    }

    protected override void OnClosed(EventArgs e)
    {
        Settings.Changed -= OnSettingsChanged;
        _settingsWindow?.Close();
        _tray?.Dispose();
        _visualizer.Dispose();
        StopSystem();
        base.OnClosed(e);
    }

    void OnSettingsChanged()
    {
        Theme.Apply(Resources);
        Marquee.Enabled = Settings.Get(Feature.Marquee);
        CompactTitle.Restart();
        TitleText.Restart();
        Refresh();
    }

    // ---------- State ----------

    void Refresh()
    {
        if (_expand == ExpandKind.Timer && _timer is not (TimerState.Running or TimerState.Paused)
            || _expand == ExpandKind.Media && _state == null)
            _expand = ExpandKind.None;

        var hover = _hover && !_hoverSuppressed;
        var showMedia = _state != null && (Settings.AlwaysShowMedia || _mediaFlash || _peek || hover);
        var showTimer = TimerActive && (_timerFlash || _peek || hover || _timerUrgent || _timer == TimerState.Done);
        var showDownload = _download != null && Settings.Get(Feature.Downloads) && (_downloadFlash || _peek || hover);
        var showShelf = _shelf.Count > 0 && Settings.Get(Feature.Shelf) && (_peek || hover);
        var showStats = Settings.Get(Feature.SystemStats) && (_peek || hover);

        double w, h, r, shadow = 0;
        FrameworkElement? panel;

        if (_expand == ExpandKind.Timer)
            (w, h, r, shadow, panel) = (380, 120, 44, 0.5, TimerExpanded);
        else if (_expand == ExpandKind.Media)
            (w, h, r, shadow, panel) = (380, 180, 44, 0.5, ExpandedMedia);
        else if (_expand == ExpandKind.Clipboard)
            (w, h, r, shadow, panel) = (380, 260, 40, 0.5, ClipboardPanel);
        else if (_expand == ExpandKind.Shelf)
            (w, h, r, shadow, panel) = (380, 170, 40, 0.5, ShelfPanel);
        else if (_transient == Transient.Notification)
            (w, h, r, shadow, panel) = (360, 74, 30, 0.35, NotificationPanel);
        else if (_transient == Transient.Screenshot)
            (w, h, r, shadow, panel) = (330, 74, 30, 0.35, ScreenshotPanel);
        else if (_transient == Transient.Volume)
            (w, h, r, panel) = (250, PillHeight, PillRadius, VolumePanel);
        else if (_transient == Transient.Battery)
            (w, h, r, panel) = (290, PillHeight, PillRadius, BatteryPanel);
        else if (_transient == Transient.Lock)
            (w, h, r, panel) = (250, PillHeight, PillRadius, LockPanel);
        else if (_transient == Transient.DownloadDone)
            (w, h, r, panel) = (300, PillHeight, PillRadius, DownloadPanel);
        else if (_transient is Transient.Privacy or Transient.Game or Transient.Update)
            (w, h, r, panel) = (320, PillHeight, PillRadius, InfoPanel);
        else if (showTimer)
            (w, h, r, panel) = (230, PillHeight, PillRadius, TimerCompact);
        else if (showDownload)
            (w, h, r, panel) = (300, PillHeight, PillRadius, DownloadPanel);
        else if (showMedia)
            (w, h, r, panel) = (300, PillHeight, PillRadius, CompactMedia);
        else if (showShelf)
            (w, h, r, panel) = (230, PillHeight, PillRadius, ShelfCompact);
        else if (showStats)
            (w, h, r, panel) = (300, PillHeight, PillRadius, StatsPanel);
        else
            (w, h, r, panel) = (126, PillHeight, PillRadius, null);

        if (hover && h == PillHeight)
        {
            w += 8;
            h += 2;
            r += 1;
        }

        _width.Target = w;
        _height.Target = h;
        _radius.Target = r;
        _shadow.Target = shadow;

        // Hidden unless there is something to show, so it never sits on top of browser tabs.
        var visible = !_fullscreen
                      && (Expanded || _transient != Transient.None || showMedia || showTimer || showDownload
                          || showShelf || showStats || _peek || hover);
        _visible.Target = visible ? 1 : 0;

        if (panel != _panel)
            SwitchPanel(panel);

        UpdateBubble(visible);

        _progressTimer.IsEnabled = _expand == ExpandKind.Media;
        if (_panel == StatsPanel && !_statsTimer.IsEnabled)
        {
            ShowStats();
            _statsTimer.Start();
        }
        else if (_panel != StatsPanel)
        {
            _statsTimer.Stop();
        }

        if (BarsActive && Settings.Get(Feature.RealWaveform))
            _visualizer.Start();
        else
            _visualizer.Stop();

        EnsureLoop();
    }

    bool BarsActive => _state?.IsPlaying == true && (_panel == CompactMedia || _panel == ExpandedMedia);

    /// <summary>A second live activity sits in a small circle beside the pill, like iOS.</summary>
    void UpdateBubble(bool visible)
    {
        var kind = BubbleKind.None;
        var downloadOn = _download != null && Settings.Get(Feature.Downloads);
        if (_panel == TimerCompact)
            kind = _state != null ? BubbleKind.Media : downloadOn ? BubbleKind.Download : BubbleKind.None;
        else if (_panel == DownloadPanel && _transient != Transient.DownloadDone)
            kind = TimerActive ? BubbleKind.Timer : _state != null ? BubbleKind.Media : BubbleKind.None;
        else if (_panel == CompactMedia)
            kind = TimerActive ? BubbleKind.Timer : downloadOn ? BubbleKind.Download : BubbleKind.None;

        _bubbleKind = kind;
        _bubble.Target = visible && kind != BubbleKind.None ? 1 : 0;

        BubbleArtCircle.Visibility = kind == BubbleKind.Media ? Visibility.Visible : Visibility.Collapsed;
        BubbleRing.Visibility = kind == BubbleKind.Timer ? Visibility.Visible : Visibility.Collapsed;
        BubbleIcon.Visibility = kind == BubbleKind.Download ? Visibility.Visible : Visibility.Collapsed;
        if (kind == BubbleKind.Media)
            BubbleArt.ImageSource = CompactArt.ImageSource;
    }

    void Expand(ExpandKind kind)
    {
        _expand = kind;
        if (kind != ExpandKind.None)
        {
            // An open card takes over; whatever pill was showing comes back afterwards.
            if (_transient != Transient.None && _transientApply != null)
                Park(_transient, _transientEnds - DateTime.Now, _transientApply);
            _transientTimer.Stop();
            _transient = Transient.None;
            _transientApply = null;
        }
        if (kind == ExpandKind.Media)
            UpdateProgress();
        if (kind == ExpandKind.Clipboard)
            BuildClipboardList();
        if (kind == ExpandKind.Shelf)
            BuildShelf();
        if (kind == ExpandKind.None && _pending.Count > 0)
        {
            ShowNextPending();
            return;
        }
        Refresh();
    }

    // ---------- Event queue ----------

    /// <summary>Higher wins. Instant feedback (volume, lock keys) always shows but never waits in line.</summary>
    static int Priority(Transient kind) => kind switch
    {
        Transient.Battery => 5,
        Transient.Privacy => 4,
        Transient.Notification or Transient.Screenshot => 3,
        Transient.DownloadDone or Transient.Game or Transient.Update => 2,
        _ => 1,
    };

    /// <summary>
    /// Shows a short-lived pill. <paramref name="apply"/> fills in its content and runs when the pill
    /// actually appears, so queued events still show their own text later.
    /// </summary>
    void ShowTransient(Transient kind, TimeSpan duration, Action apply)
    {
        if (_fullscreen)
            return;
        if (Expanded)
        {
            Park(kind, duration, apply);
            return;
        }

        if (_transient != Transient.None && _transient != kind)
        {
            var current = Priority(_transient);
            var incoming = Priority(kind);
            if (incoming < current && incoming > 1)
            {
                Park(kind, duration, apply);
                return;
            }
            // The newcomer wins: put the interrupted pill back in line with its remaining time.
            if (_transientApply != null)
                Park(_transient, _transientEnds - DateTime.Now, _transientApply);
        }

        apply();
        _transient = kind;
        _transientApply = apply;
        _transientEnds = DateTime.Now + duration;
        _transientTimer.Stop();
        _transientTimer.Interval = duration;
        _transientTimer.Start();
        Refresh();
    }

    void Park(Transient kind, TimeSpan remaining, Action apply)
    {
        if (Priority(kind) <= 1 || remaining < TimeSpan.FromSeconds(1.5))
            return;
        _pending.RemoveAll(p => p.Kind == kind);
        _pending.Add(new PendingTransient(kind, remaining, apply, DateTime.Now));
    }

    void EndTransient()
    {
        _transientTimer.Stop();
        _transient = Transient.None;
        _transientApply = null;
        ShowNextPending();
    }

    void ShowNextPending()
    {
        // Old news isn't worth showing.
        _pending.RemoveAll(p => DateTime.Now - p.Since > TimeSpan.FromSeconds(20));
        if (_pending.Count > 0 && !Quiet)
        {
            var next = _pending.OrderByDescending(p => Priority(p.Kind)).First();
            _pending.Remove(next);
            ShowTransient(next.Kind, next.Duration, next.Apply);
            return;
        }
        Refresh();
    }

    void DismissTransient() => EndTransient();

    // ---------- Panels & animation ----------

    void SwitchPanel(FrameworkElement? next)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var panel in _panels)
        {
            var on = panel == next;
            var wasOn = panel == _panel;
            if (!on && !wasOn)
                continue;

            panel.IsHitTestVisible = on;
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 300 : 120))
            {
                BeginTime = TimeSpan.FromMilliseconds(on ? 90 : 0),
                EasingFunction = ease,
            });

            if (!on)
                continue;

            var grow = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(420))
            {
                BeginTime = TimeSpan.FromMilliseconds(90),
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut },
            };
            var scale = (ScaleTransform)panel.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            // Content comes into focus as it appears.
            var blur = new BlurEffect { Radius = 10, RenderingBias = RenderingBias.Performance };
            panel.Effect = blur;
            var sharpen = new DoubleAnimation(0, TimeSpan.FromMilliseconds(320))
            {
                BeginTime = TimeSpan.FromMilliseconds(90),
                EasingFunction = ease,
            };
            sharpen.Completed += (_, _) =>
            {
                if (panel.Effect == blur)
                    panel.Effect = null;
            };
            blur.BeginAnimation(BlurEffect.RadiusProperty, sharpen);
        }
        _panel = next;
    }

    void EnsureLoop()
    {
        if (_looping)
            return;
        _looping = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame)
            return;
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 20.0);
        _lastFrame = now;

        var barsActive = BarsActive;
        var levels = barsActive && _visualizer.IsRunning ? _visualizer.Levels : null;
        var t = now.TotalSeconds;
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i].Target = !barsActive ? 0.25
                : levels != null ? 0.18 + 0.82 * levels[i]
                : IdleWave(t, i);
        }

        _width.Step(dt);
        _height.Step(dt);
        _radius.Step(dt);
        _shadow.Step(dt);
        _visible.Step(dt);
        _shiftX.Step(dt);
        _bubble.Step(dt);
        foreach (var bar in _bars)
            bar.Step(dt);

        Apply();

        var settled = _width.AtRest && _height.AtRest && _radius.AtRest && _shadow.AtRest && _visible.AtRest
                      && _shiftX.AtRest && _bubble.AtRest && _bars.All(b => b.AtRest);
        if (settled && !barsActive)
        {
            CompositionTarget.Rendering -= OnFrame;
            _looping = false;
        }
    }

    /// <summary>Smooth, overlapping sine waves: a calm "playing" motion when the real audio isn't available.</summary>
    static double IdleWave(double t, int bar)
    {
        var v = 0.55 + 0.3 * Math.Sin(t * (3.6 + bar * 1.1) + bar * 1.9) + 0.15 * Math.Sin(t * (6.3 + bar * 0.7) + bar);
        return Math.Clamp(v, 0.2, 1);
    }

    void Apply()
    {
        var w = Math.Max(_width.Value, 40);
        var h = Math.Max(_height.Value, 20);
        var r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);

        Island.Width = w;
        Island.Height = h;
        Island.CornerRadius = new CornerRadius(r);
        Stage.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        Shadow.Opacity = Math.Clamp(_shadow.Value, 0, 1);

        var shift = _shiftX.Value;
        var v = Math.Clamp(_visible.Value, 0, 1) * Math.Clamp(1 - Math.Abs(shift) / 260, 0, 1);
        Island.Opacity = v;
        IslandScale.ScaleX = IslandScale.ScaleY = 0.7 + 0.3 * Math.Clamp(_visible.Value, 0, 1);
        IslandShift.X = shift;
        Island.Visibility = _visible.Target == 0 && v < 0.02 ? Visibility.Hidden : Visibility.Visible;

        var b = Math.Clamp(_bubble.Value, 0, 1) * v;
        Bubble.Opacity = b;
        BubbleScale.ScaleX = BubbleScale.ScaleY = 0.5 + 0.5 * Math.Clamp(_bubble.Value, 0, 1);
        BubbleShift.X = w / 2 + BubbleGap + Bubble.Width / 2 + shift;
        Bubble.Visibility = b < 0.02 ? Visibility.Hidden : Visibility.Visible;

        foreach (var panel in _panels)
        {
            Canvas.SetLeft(panel, (w - panel.Width) / 2);
            Canvas.SetTop(panel, (h - panel.Height) / 2);
        }

        for (var i = 0; i < _bars.Length; i++)
        {
            var level = Math.Clamp(_bars[i].Value, 0.15, 1);
            _compactBars[i].Height = level * 14;
            _expandedBars[i].Height = level * 20;
        }
    }

    Rectangle[] CreateBars(Panel host, double width, double gap)
    {
        var bars = new Rectangle[_bars.Length];
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i] = new Rectangle
            {
                Width = width,
                RadiusX = width / 2,
                RadiusY = width / 2,
                Fill = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : gap, 0, 0, 0),
            };
            host.Children.Add(bars[i]);
        }
        return bars;
    }

    // ---------- Revealing ----------

    /// <summary>
    /// Polls the pointer instead of using an invisible hit strip, so clicks at the top edge
    /// (browser tabs) always reach the window underneath.
    /// </summary>
    void PollCursor()
    {
        var leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        var buttonDown = leftDown || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        UpdateDropZone(leftDown);

        if (!GetCursorPos(out var device))
            return;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;

        var p = source.CompositionTarget.TransformFromDevice.Transform(new Point(device.X, device.Y));
        _inPeekZone = !_fullscreen && p.Y <= Top + 2 && Math.Abs(p.X - (Left + Width / 2)) <= PeekZoneHalfWidth;

        if (!_inPeekZone)
        {
            _dwell = TimeSpan.Zero;
            _peekSuppressed = false;
        }
        else if (buttonDown)
        {
            // They were aiming at something underneath; stay hidden until the pointer leaves the strip.
            _dwell = TimeSpan.Zero;
            _peekSuppressed = true;
        }
        else if (!_peekSuppressed && !_peek)
        {
            _dwell += _cursorTimer.Interval;
            if (_dwell >= PeekDwell)
            {
                _peek = true;
                _hoverSuppressed = false;
                _peekHideTimer.Stop();
                Refresh();
            }
        }

        if (_peek && !_inPeekZone && !Island.IsMouseOver && !_peekHideTimer.IsEnabled)
            _peekHideTimer.Start();
    }

    void CheckFullscreen()
    {
        var fullscreen = FullscreenDetector.IsActive(new WindowInteropHelper(this).Handle);
        if (fullscreen == _fullscreen)
            return;

        _fullscreen = fullscreen;
        if (fullscreen)
        {
            _fullscreenSince = DateTime.Now;
            _fullscreenApp = FullscreenDetector.ForegroundProcessName();
            _expand = ExpandKind.None;
            _peek = false;
            _transientTimer.Stop();
            _transient = Transient.None;
            _pending.Clear();
            Refresh();
            return;
        }

        // Some games drop other windows out of the topmost band; claim it back.
        Topmost = false;
        Topmost = true;
        Refresh();
        ShowGameSummary(DateTime.Now - _fullscreenSince, _fullscreenApp);
    }

    // ---------- Input ----------

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _hover = true;
        _collapseTimer.Stop();
        _peekHideTimer.Stop();
        Refresh();
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _hover = false;
        _hoverSuppressed = false;
        if (Expanded)
            _collapseTimer.Start();
        if (_peek)
            _peekHideTimer.Start();
        Refresh();
    }

    void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(this);
        _pressing = true;
        _swiping = false;
    }

    void Island_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressing || e.LeftButton != MouseButtonState.Pressed)
            return;

        var dx = e.GetPosition(this).X - _pressPoint.X;
        if (!_swiping && Math.Abs(dx) > 6)
        {
            _swiping = true;
            Island.CaptureMouse();
        }
        if (_swiping)
        {
            // Follows the finger with a little resistance, like iOS.
            _shiftX.Value = _shiftX.Target = dx * 0.85;
            _shiftX.Velocity = 0;
            Apply();
        }
    }

    void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var wasSwipe = _swiping;
        _pressing = false;
        _swiping = false;

        if (wasSwipe)
        {
            Island.ReleaseMouseCapture();
            if (Math.Abs(_shiftX.Value) > SwipeDismissDistance)
                SwipeAway(Math.Sign(_shiftX.Value));
            else
                _shiftX.Target = 0;
            EnsureLoop();
            return;
        }

        if (Expanded)
        {
            Expand(ExpandKind.None);
            return;
        }

        if (_panel == TimerCompact)
        {
            if (_timer == TimerState.Done)
                StopTimer();
            else
                Expand(ExpandKind.Timer);
        }
        else if (_panel == NotificationPanel)
        {
            OpenNotificationApp();
            DismissTransient();
        }
        else if (_panel == ScreenshotPanel)
        {
            SaveAndOpenScreenshot();
            DismissTransient();
        }
        else if (_panel == DownloadPanel && _transient == Transient.DownloadDone)
        {
            RevealDownload();
            DismissTransient();
        }
        else if (_panel == InfoPanel)
        {
            if (_transient == Transient.Update)
                InstallUpdate();
            else
                DismissTransient();
        }
        else if (_panel == ShelfCompact)
        {
            Expand(ExpandKind.Shelf);
        }
        else if (_state != null && _panel != StatsPanel)
        {
            Expand(ExpandKind.Media);
        }
        else if (_timer is TimerState.Running or TimerState.Paused)
        {
            Expand(ExpandKind.Timer);
        }
    }

    /// <summary>Slides the island off to the side and dismisses what it was showing.</summary>
    void SwipeAway(int direction)
    {
        _shiftX.Target = direction * 260;
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            _hoverSuppressed = true;
            _peek = false;
            _mediaFlash = _timerFlash = _downloadFlash = false;
            _expand = ExpandKind.None;
            EndTransient();

            // Snap back to the middle without being seen.
            _shiftX.Value = _shiftX.Target = _shiftX.Velocity = 0;
            if (_visible.Target == 0)
                _visible.Value = _visible.Velocity = 0;
            Apply();
        };
        settle.Start();
    }

    void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        switch (_bubbleKind)
        {
            case BubbleKind.Media:
                Expand(ExpandKind.Media);
                break;
            case BubbleKind.Timer:
                Expand(ExpandKind.Timer);
                break;
            case BubbleKind.Download:
                OpenDownloadsFolder();
                break;
        }
    }

    void ContextMenu_Opened(object sender, RoutedEventArgs e) => StopTimerItem.IsEnabled = TimerActive;

    void ClipboardHistory_Click(object sender, RoutedEventArgs e) => Expand(ExpandKind.Clipboard);

    void ShelfMenu_Click(object sender, RoutedEventArgs e) => Expand(ExpandKind.Shelf);

    void SettingsMenu_Click(object sender, RoutedEventArgs e) => OpenSettings();

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void OpenSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(() => CheckForUpdatesAsync(manual: true));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    // ---------- Win32 ----------

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int VK_LBUTTON = 0x01;
    const int VK_RBUTTON = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);
}
