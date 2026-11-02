using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland;

// Now playing: compact pill with the song title, expanded card with seeking and an artwork-colored glow.
public partial class MainWindow
{
    readonly MediaService _media = new();
    readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer _mediaFlashTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    MediaState? _state;
    bool _mediaFlash;   // briefly show the music pill after a track or play/pause change
    bool _seeking;      // dragging the progress bar
    Color _accent = Colors.White;

    void InitMedia()
    {
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _mediaFlashTimer.Tick += (_, _) =>
        {
            _mediaFlashTimer.Stop();
            _mediaFlash = false;
            Refresh();
        };
        _media.Changed += OnMediaChanged;
    }

    async void StartMedia()
    {
        try { await _media.StartAsync(); } catch { /* media controls unavailable */ }
    }

    void OnMediaChanged(MediaState? state)
    {
        var artChanged = !ReferenceEquals(state?.Art, _state?.Art);
        var noteworthy = state != null
                         && (state.Title != _state?.Title || state.Artist != _state?.Artist || state.IsPlaying != _state?.IsPlaying);
        _state = state;

        if (noteworthy && !_fullscreen)
        {
            _mediaFlash = true;
            _mediaFlashTimer.Stop();
            _mediaFlashTimer.Start();
        }

        if (state != null)
        {
            CompactTitle.Text = state.Title;
            TitleText.Text = state.Title;
            ArtistText.Text = state.Artist;
            PlayIcon.Visibility = state.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
            PauseIcon.Visibility = state.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

            if (artChanged)
            {
                CompactArt.ImageSource = state.Art;
                ExpandedArt.ImageSource = state.Art;
                _accent = state.Art != null ? AccentFrom(state.Art) : Colors.White;

                var accent = new SolidColorBrush(_accent);
                accent.Freeze();
                foreach (var bar in _compactBars.Concat(_expandedBars))
                    bar.Fill = accent;
                // A faint tint around the cover only; the island's own shadow stays neutral.
                GlowStop.Color = state.Art != null ? Color.FromArgb(0x38, _accent.R, _accent.G, _accent.B) : Colors.Transparent;
                GlowMid.Color = state.Art != null ? Color.FromArgb(0x12, _accent.R, _accent.G, _accent.B) : Colors.Transparent;
            }
            UpdateProgress();
        }

        Refresh();
    }

    void UpdateProgress()
    {
        if (_seeking)
            return;
        if (_state is not { } s || s.Duration <= TimeSpan.Zero)
        {
            ProgressFill.Width = 0;
            ElapsedText.Text = RemainingText.Text = "";
            return;
        }

        var position = s.Position;
        // Players report a position plus the moment it was sampled; extrapolate while playing.
        if (s.IsPlaying && s.UpdatedAt.Year > 2000)
            position += DateTimeOffset.Now - s.UpdatedAt;
        position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, s.Duration.Ticks));
        ShowPosition(position / s.Duration, s.Duration);
    }

    void ShowPosition(double fraction, TimeSpan duration)
    {
        var position = duration * fraction;
        ProgressFill.Width = Track.ActualWidth * fraction;
        ElapsedText.Text = FormatTime(position);
        RemainingText.Text = "-" + FormatTime(duration - position);
    }

    static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ---------- Seeking ----------

    double TrackFraction(MouseEventArgs e) =>
        Track.ActualWidth > 0 ? Math.Clamp(e.GetPosition(Track).X / Track.ActualWidth, 0, 1) : 0;

    void Track_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_state is not { Duration.Ticks: > 0 } s)
            return;
        _seeking = true;
        Track.CaptureMouse();
        ShowPosition(TrackFraction(e), s.Duration);
    }

    void Track_MouseMove(object sender, MouseEventArgs e)
    {
        if (_seeking && _state is { } s)
            ShowPosition(TrackFraction(e), s.Duration);
    }

    async void Track_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Handled so the island's own click doesn't collapse the card.
        e.Handled = true;
        if (!_seeking)
            return;
        _seeking = false;
        Track.ReleaseMouseCapture();

        if (_state is not { } s)
            return;
        var target = s.Duration * TrackFraction(e);
        // Show the new position right away; the player confirms it with its next timeline update.
        _state = s with { Position = target, UpdatedAt = DateTimeOffset.Now };
        UpdateProgress();
        await _media.SeekAsync(target);
    }

    /// <summary>Saturation-weighted average of the artwork, brightened — like iOS tinting the waveform.</summary>
    static Color AccentFrom(BitmapSource art)
    {
        var small = new TransformedBitmap(art, new ScaleTransform(8.0 / art.PixelWidth, 8.0 / art.PixelHeight));
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        double r = 0, g = 0, b = 0, total = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            double pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
            var weight = (Math.Max(pr, Math.Max(pg, pb)) - Math.Min(pr, Math.Min(pg, pb))) / 255.0 + 0.05;
            r += pr * weight;
            g += pg * weight;
            b += pb * weight;
            total += weight;
        }
        r /= total;
        g /= total;
        b /= total;

        var max = Math.Max(r, Math.Max(g, b));
        var k = max < 1 ? 1 : 235 / max;
        return Color.FromRgb((byte)Math.Min(255, r * k), (byte)Math.Min(255, g * k), (byte)Math.Min(255, b * k));
    }

    async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    async void Previous_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();
}
