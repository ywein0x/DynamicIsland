using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

public sealed record FavoriteSong(string Title, string Artist, string AppId, DateTime Added, string? ArtFile, string? SpotifyUri = null);

/// <summary>Liked songs, kept in a file on this PC with a small copy of each cover.</summary>
public sealed class FavoritesStore
{
    readonly List<FavoriteSong> _songs = Load();

    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "favorites");

    static string FilePath => Path.Combine(Folder, "favorites.json");

    public IReadOnlyList<FavoriteSong> Songs => _songs;

    public bool Contains(string title, string artist) => Find(title, artist) != null;

    public void Add(MediaState state, string? spotifyUri = null)
    {
        if (Contains(state.Title, state.Artist))
            return;
        _songs.Insert(0, new FavoriteSong(state.Title, state.Artist, state.AppId, DateTime.Now, SaveArt(state.Art), spotifyUri));
        Save();
    }

    public void Remove(string title, string artist)
    {
        if (Find(title, artist) is not { } song)
            return;
        _songs.Remove(song);
        if (song.ArtFile != null)
        {
            try { File.Delete(Path.Combine(Folder, song.ArtFile)); }
            catch { /* a leftover picture is harmless */ }
        }
        Save();
    }

    public static BitmapSource? LoadArt(FavoriteSong song)
    {
        if (song.ArtFile == null)
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.Combine(Folder, song.ArtFile));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    FavoriteSong? Find(string title, string artist) => _songs.FirstOrDefault(s =>
        string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase) && string.Equals(s.Artist, artist, StringComparison.OrdinalIgnoreCase));

    static string? SaveArt(BitmapSource? art)
    {
        if (art == null)
            return null;
        try
        {
            Directory.CreateDirectory(Folder);
            var name = Guid.NewGuid().ToString("N") + ".png";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(art));
            using var file = File.Create(Path.Combine(Folder, name));
            encoder.Save(file);
            return name;
        }
        catch
        {
            return null;
        }
    }

    static List<FavoriteSong> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<FavoriteSong>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch
        {
            return [];
        }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_songs));
        }
        catch
        {
            // Stays in memory until the app closes.
        }
    }
}
