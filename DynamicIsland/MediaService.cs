using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace DynamicIsland;

public sealed record MediaState(
    string Title,
    string Artist,
    BitmapSource? Art,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Follows whatever Windows shows in its media flyout (Spotify, browsers, Apple Music, ...).
/// <see cref="Changed"/> is raised on the UI thread; null means nothing is playing or paused.
/// </summary>
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    Session? _session;
    string? _artKey;
    BitmapSource? _art;

    public event Action<MediaState?>? Changed;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => Post(Attach);
        Attach();
    }

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task NextAsync() => Run(s => s.TrySkipNextAsync().AsTask());
    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync().AsTask());

    public Task SeekAsync(TimeSpan position) =>
        Run(s => s.TryChangePlaybackPositionAsync((s.GetTimelineProperties().StartTime + position).Ticks).AsTask());

    async Task Run(Func<Session, Task<bool>> action)
    {
        if (_session is { } session)
        {
            try { await action(session); } catch { /* player went away */ }
        }
    }

    static void Post(Action action) => Application.Current.Dispatcher.InvokeAsync(action);

    void Attach()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = _manager?.GetCurrentSession();

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }

        _ = RefreshAsync();
    }

    void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => Post(() => _ = RefreshAsync());
    void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) => Post(() => _ = RefreshAsync());
    void OnTimelinePropertiesChanged(Session sender, TimelinePropertiesChangedEventArgs args) => Post(() => _ = RefreshAsync());

    async Task RefreshAsync()
    {
        var session = _session;
        if (session == null)
        {
            Changed?.Invoke(null);
            return;
        }

        try
        {
            var status = session.GetPlaybackInfo().PlaybackStatus;
            if (status is not (PlaybackStatus.Playing or PlaybackStatus.Paused))
            {
                Changed?.Invoke(null);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            if (!IsMusic(session.SourceAppUserModelId, props))
            {
                Changed?.Invoke(null);
                return;
            }

            var key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
            if (key != _artKey)
            {
                var art = props.Thumbnail != null ? await LoadArtAsync(props.Thumbnail) : null;
                (_artKey, _art) = (key, art);
            }

            if (session != _session)
                return;

            var timeline = session.GetTimelineProperties();
            Changed?.Invoke(new MediaState(
                props.Title,
                props.Artist,
                _art,
                session.GetPlaybackInfo().PlaybackStatus == PlaybackStatus.Playing,
                timeline.Position,
                timeline.EndTime - timeline.StartTime,
                timeline.LastUpdatedTime));
        }
        catch
        {
            Changed?.Invoke(null);
        }
    }

    static readonly string[] MusicApps =
        ["spotify", "music", "itunes", "deezer", "tidal", "foobar", "aimp", "musicbee", "winamp", "soundcloud", "amazon"];

    static readonly string[] Browsers = ["chrome", "msedge", "brave", "opera", "firefox", "vivaldi", "arc"];

    /// <summary>
    /// Music only: YouTube videos and Shorts in a browser shouldn't pop the island on every swipe.
    /// Music players always count; in a browser it must look like a song (YouTube Music and the
    /// Spotify web player send an album, plain YouTube doesn't).
    /// </summary>
    static bool IsMusic(string appId, GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        var app = appId.ToLowerInvariant();
        if (MusicApps.Any(app.Contains))
            return true;
        if (!string.IsNullOrWhiteSpace(props.AlbumTitle))
            return true;
        if (Browsers.Any(app.Contains))
            return false;
        return props.PlaybackType == Windows.Media.MediaPlaybackType.Music;
    }

    static async Task<BitmapSource?> LoadArtAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var source = stream.AsStreamForRead();
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 200;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
