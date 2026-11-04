using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

// Countdown timer live activity: compact ring + time, expanded controls, alarm when done.
public partial class MainWindow
{
    enum TimerState { Off, Running, Paused, Done }

    readonly DispatcherTimer _timerTick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly DispatcherTimer _timerFlashTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly DispatcherTimer _timerDoneTimer = new() { Interval = TimeSpan.FromSeconds(12) };

    TimerState _timer;
    DateTime _timerEnd;
    TimeSpan _timerLeft, _timerTotal;
    bool _timerFlash;   // briefly show the pill after starting
    bool _timerUrgent;  // last 10 seconds: stay on screen
    SoundPlayer? _alarm;

    bool TimerActive => _timer != TimerState.Off;

    TimeSpan TimerRemaining => _timer == TimerState.Running
        ? TimeSpan.FromTicks(Math.Max(0, (_timerEnd - DateTime.Now).Ticks))
        : _timerLeft;

    void InitTimer()
    {
        _timerTick.Tick += (_, _) => UpdateTimer();
        _timerFlashTimer.Tick += (_, _) =>
        {
            _timerFlashTimer.Stop();
            _timerFlash = false;
            Refresh();
        };
        _timerDoneTimer.Tick += (_, _) => StopTimer();
    }

    void StartTimer(TimeSpan duration)
    {
        _timerTotal = duration;
        _timerEnd = DateTime.Now + duration;
        _timer = TimerState.Running;
        _timerDoneTimer.Stop();
        _timerTick.Start();

        _timerFlash = true;
        _timerFlashTimer.Stop();
        _timerFlashTimer.Start();

        UpdateTimer();
        Refresh();
    }

    void StopTimer()
    {
        _timer = TimerState.Off;
        _timerTick.Stop();
        _timerDoneTimer.Stop();
        _timerUrgent = false;
        _alarm?.Stop();
        if (_expand == ExpandKind.Timer)
            _expand = ExpandKind.None;
        Refresh();
    }

    void UpdateTimer()
    {
        var left = TimerRemaining;
        if (_timer == TimerState.Running && left <= TimeSpan.Zero)
        {
            FinishTimer();
            return;
        }

        var text = FormatCountdown(left);
        TimerCompactText.Text = text;
        TimerBigText.Text = text;
        TimerRing.Visibility = Visibility.Visible;
        TimerDoneIcon.Visibility = Visibility.Collapsed;
        TimerRingArc.Data = RingArc(10, 10, 8.75, _timerTotal > TimeSpan.Zero ? left / _timerTotal : 0);
        BubbleRingArc.Data = TimerRingArc.Data;
        TimerPauseIcon.Visibility = _timer == TimerState.Running ? Visibility.Visible : Visibility.Collapsed;
        TimerResumeIcon.Visibility = _timer == TimerState.Paused ? Visibility.Visible : Visibility.Collapsed;

        var urgent = _timer == TimerState.Running && left <= TimeSpan.FromSeconds(10);
        if (urgent != _timerUrgent)
        {
            _timerUrgent = urgent;
            Refresh();
        }
    }

    void FinishTimer()
    {
        _timer = TimerState.Done;
        _timerLeft = TimeSpan.Zero;
        _timerTick.Stop();
        _timerUrgent = false;

        TimerCompactText.Text = "Süre doldu";
        TimerRing.Visibility = Visibility.Collapsed;
        TimerDoneIcon.Visibility = Visibility.Visible;

        if (_expand == ExpandKind.Timer)
            _expand = ExpandKind.None;
        Refresh();
        _width.Velocity += 500; // springy "ding" bounce
        EnsureLoop();

        PlayAlarm();
        _timerDoneTimer.Start();
    }

    void PlayAlarm()
    {
        if (!Settings.Get(Feature.TimerSound))
            return;
        var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Alarm01.wav");
        try
        {
            if (File.Exists(wav))
            {
                _alarm = new SoundPlayer(wav);
                _alarm.Play();
                return;
            }
        }
        catch { /* fall back to the system sound */ }
        SystemSounds.Asterisk.Play();
    }

    static string FormatCountdown(TimeSpan t)
    {
        t = TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>Clockwise arc from 12 o'clock covering <paramref name="fraction"/> of a circle.</summary>
    static Geometry RingArc(double cx, double cy, double radius, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        if (fraction <= 0)
            return Geometry.Empty;

        var angle = fraction * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(cx, cy - radius), false, false);
            ctx.ArcTo(new Point(cx + radius * Math.Sin(angle), cy - radius * Math.Cos(angle)),
                new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    void StartTimer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string minutes })
            return;
        StartTimer(TimeSpan.FromMinutes(int.Parse(minutes)));
        // Back to the shortcuts; the new timer now has its card on the home menu.
        ShowAndFitChips(false);
    }

    void TimerPause_Click(object sender, RoutedEventArgs e)
    {
        if (_timer == TimerState.Running)
        {
            _timerLeft = TimerRemaining;
            _timer = TimerState.Paused;
        }
        else if (_timer == TimerState.Paused)
        {
            _timerEnd = DateTime.Now + _timerLeft;
            _timer = TimerState.Running;
        }
        UpdateTimer();
    }

    void TimerCancel_Click(object sender, RoutedEventArgs e) => StopTimer();
}
