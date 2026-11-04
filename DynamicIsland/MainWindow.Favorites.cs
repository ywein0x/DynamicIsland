using System.Diagnostics;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

// Liking songs: a list kept on this PC, and Spotify's own Liked Songs through its Web API when connected.
public partial class MainWindow
{
    const int FavoritesShown = 6;
    const string HeartEmpty = "";
    const string HeartFull = "";
    static readonly Color HeartColor = Color.FromRgb(0xFF, 0x37, 0x5F);

    readonly FavoritesStore _favorites = new();

    SpotifyTrack? _spotifyTrack; // the playing song as Spotify knows it; null when not connected or not found
    bool _spotifyLiked, _spotifyConnected;

    static bool FromSpotify(string appId) => appId.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    bool IsLiked(MediaState song) => _spotifyTrack != null ? _spotifyLiked : _favorites.Contains(song.Title, song.Artist);

    /// <summary>Asks Spotify about the song that just started, so the heart tells the truth.</summary>
    async Task SyncSpotifyAsync(MediaState song)
    {
        _spotifyConnected = SpotifyClient.Connected;
        _spotifyTrack = null;
        UpdateHearts();
        if (!_spotifyConnected || !FromSpotify(song.AppId))
            return;

        try
        {
            // Spotify's servers hear about a new song a moment after the app starts playing it.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(1500);
                if (_state?.Title != song.Title)
                    return;
                var track = await SpotifyClient.CurrentTrackAsync();
                if (track == null || !string.Equals(track.Title, song.Title, StringComparison.OrdinalIgnoreCase))
                    continue;

                var liked = await SpotifyClient.IsLikedAsync(track.Uri);
                if (_state?.Title != song.Title)
                    return;
                _spotifyTrack = track;
                _spotifyLiked = liked;
                UpdateHearts();
                return;
            }
        }
        catch
        {
            // No internet or an expired sign-in: the heart falls back to the list on this PC.
        }
    }

    async void Heart_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_state is not { } song)
            return;

        var track = _spotifyTrack;
        var like = !IsLiked(song);
        SetLocalLike(song, like, track?.Uri);
        _spotifyLiked = like;
        UpdateHearts();

        if (track == null)
        {
            if (SpotifyClient.Connected && FromSpotify(song.AppId))
                ShowSpotifyNotice("Spotify'da bulunamadı", "Sadece adadaki listeye eklendi");
            return;
        }

        try
        {
            await SpotifyClient.SetLikedAsync(track.Uri, like);
        }
        catch
        {
            // Spotify didn't take it: show the song as it really is.
            SetLocalLike(song, !like, track.Uri);
            if (_spotifyTrack == track)
                _spotifyLiked = !like;
            UpdateHearts();
            ShowSpotifyNotice("Spotify'a ulaşılamadı", "Beğeni kaydedilmedi");
        }
    }

    void SetLocalLike(MediaState song, bool like, string? uri)
    {
        if (like)
            _favorites.Add(song, uri);
        else
            _favorites.Remove(song.Title, song.Artist);
    }

    void ShowSpotifyNotice(string title, string detail) =>
        ShowTransient(Transient.Privacy, TimeSpan.FromSeconds(3.5), InfoContent(title, detail, dot: OrangeColor));

    void UpdateHearts()
    {
        var liked = _state is { } song && IsLiked(song);
        foreach (var heart in new[] { HomeHeart, MediaHeart })
        {
            heart.Text = liked ? HeartFull : HeartEmpty;
            if (liked)
                heart.Foreground = new SolidColorBrush(HeartColor);
            else
                heart.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        }
    }

    // ---------- Liked songs page ----------

    void HomeFavorites_Click(object sender, RoutedEventArgs e)
    {
        OpenFromHome(ExpandKind.Favorites);
        BuildFavorites();
    }

    void BuildFavorites()
    {
        FavoritesCount.Text = _favorites.Songs.Count == 0 ? "" : $"{_favorites.Songs.Count} şarkı";
        FavoritesList.Children.Clear();
        if (_favorites.Songs.Count == 0)
        {
            var empty = new TextBlock { Text = "Çalan şarkının kalbine basınca buraya eklenir", FontSize = 12.5, Margin = new Thickness(2, 2, 0, 8) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            FavoritesList.Children.Add(empty);
        }
        foreach (var song in _favorites.Songs.Take(FavoritesShown))
            FavoritesList.Children.Add(FavoriteRow(song));

        FavoritesContent.Measure(new Size(FavoritesPanel.Width, double.PositiveInfinity));
        FavoritesPanel.Height = Math.Ceiling(FavoritesContent.DesiredSize.Height);
        Refresh();
    }

    FrameworkElement FavoriteRow(FavoriteSong song)
    {
        var art = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        if (FavoritesStore.LoadArt(song) is { } image)
            art.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        else
            art.SetResourceReference(Border.BackgroundProperty, "ArtPlaceholder");

        var title = new TextBlock { Text = song.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        var artist = new TextBlock { Text = song.Artist, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        artist.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        text.Children.Add(title);
        text.Children.Add(artist);

        var remove = new Button { Style = (Style)FindResource("IconButton"), ToolTip = "Beğenilenlerden çıkar", VerticalAlignment = VerticalAlignment.Center };
        remove.Content = new TextBlock { Text = HeartFull, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13, Foreground = new SolidColorBrush(HeartColor) };
        remove.Click += async (_, e) =>
        {
            e.Handled = true;
            _favorites.Remove(song.Title, song.Artist);
            if (_spotifyTrack?.Uri == song.SpotifyUri)
                _spotifyLiked = false;
            UpdateHearts();
            BuildFavorites();
            if (song.SpotifyUri == null || !SpotifyClient.Connected)
                return;
            try { await SpotifyClient.SetLikedAsync(song.SpotifyUri, false); }
            catch { ShowSpotifyNotice("Spotify'a ulaşılamadı", "Spotify'daki beğeni kaldırılamadı"); }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(art);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        var row = new Border { Style = (Style)FindResource("HomeCard"), Padding = new Thickness(8, 6, 4, 6), Margin = new Thickness(0, 0, 0, 5), Cursor = Cursors.Hand, Child = grid };
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Expand(ExpandKind.None);
            OpenSong(song);
        };
        return row;
    }

    /// <summary>Finds the song again: in Spotify when it came from there, otherwise on YouTube.</summary>
    static void OpenSong(FavoriteSong song)
    {
        var query = Uri.EscapeDataString($"{song.Artist} {song.Title}".Trim());
        var target = song.AppId.Contains("spotify", StringComparison.OrdinalIgnoreCase)
            ? $"spotify:search:{query}"
            : $"https://www.youtube.com/results?search_query={query}";
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* nothing handles the link */ }
    }
}
