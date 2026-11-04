using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DynamicIsland;

public sealed record SpotifyTrack(string Uri, string Title);

/// <summary>
/// Spotify's own Web API: the only way to know whether a song is liked, and to like it for sure instead
/// of toggling blindly. Each user signs in with a Client ID of their own, since Spotify limits how many
/// people one Client ID may serve. The sign-in token stays on this PC, encrypted for this Windows user.
/// </summary>
public static class SpotifyClient
{
    public const string RedirectUri = "http://127.0.0.1:43879/callback";
    public const string DashboardUrl = "https://developer.spotify.com/dashboard";
    const int RedirectPort = 43879;
    const string Scopes = "user-library-read user-library-modify user-read-currently-playing";

    // Fields so the tests can point them at a stand-in server.
    internal static string AccountsBase = "https://accounts.spotify.com";
    internal static string ApiBase = "https://api.spotify.com/v1";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly string TokenFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "spotify.token");

    static string? _accessToken;
    static DateTime _accessTokenEnds;

    public static bool Connected => File.Exists(TokenFile) && Settings.SpotifyClientId.Length > 0;

    public static void Disconnect()
    {
        _accessToken = null;
        try { File.Delete(TokenFile); }
        catch { /* already gone */ }
        Settings.SpotifyClientId = "";
    }

    /// <summary>Opens the browser for the user to approve, and waits for Spotify to send them back here.</summary>
    public static async Task ConnectAsync(string clientId, CancellationToken cancel)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var listener = new TcpListener(IPAddress.Loopback, RedirectPort);
        listener.Start();
        try
        {
            var url = $"{AccountsBase}/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}"
                      + $"&scope={Uri.EscapeDataString(Scopes)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                      + $"&state={state}&code_challenge_method=S256&code_challenge={challenge}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            var code = await WaitForCodeAsync(listener, state, cancel);
            var token = await PostTokenAsync(new()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
            });
            Remember(token);
            Settings.SpotifyClientId = clientId; // last: tells everyone the connection is ready
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>What Spotify is playing on this account, or null when nothing is.</summary>
    public static async Task<SpotifyTrack?> CurrentTrackAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/me/player/currently-playing");
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            return null;
        return new SpotifyTrack(item.GetProperty("uri").GetString()!, item.GetProperty("name").GetString() ?? "");
    }

    public static async Task<bool> IsLikedAsync(string uri)
    {
        using var response = await SendAsync(HttpMethod.Get, "/me/library/contains?uris=" + Uri.EscapeDataString(uri));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement[0].GetBoolean();
    }

    public static async Task SetLikedAsync(string uri, bool liked)
    {
        using var response = await SendAsync(liked ? HttpMethod.Put : HttpMethod.Delete, "/me/library?uris=" + Uri.EscapeDataString(uri));
        response.EnsureSuccessStatusCode();
    }

    static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync());
        return await Http.SendAsync(request);
    }

    static async Task<string> AccessTokenAsync()
    {
        if (_accessToken != null && DateTime.Now < _accessTokenEnds)
            return _accessToken;
        var refresh = ReadRefreshToken() ?? throw new InvalidOperationException("Spotify'a bağlı değil");
        Remember(await PostTokenAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh,
            ["client_id"] = Settings.SpotifyClientId,
        }));
        return _accessToken!;
    }

    static async Task<JsonElement> PostTokenAsync(Dictionary<string, string> form)
    {
        using var response = await Http.PostAsync(AccountsBase + "/api/token", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    static void Remember(JsonElement token)
    {
        _accessToken = token.GetProperty("access_token").GetString();
        _accessTokenEnds = DateTime.Now.AddSeconds(token.GetProperty("expires_in").GetInt32() - 60);
        // Spotify may hand out a new refresh token; the old one then stops working.
        if (token.TryGetProperty("refresh_token", out var refresh) && refresh.GetString() is { } value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
            File.WriteAllBytes(TokenFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }
    }

    static string? ReadRefreshToken()
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A tiny web server for the single request of Spotify sending the browser back.</summary>
    static async Task<string> WaitForCodeAsync(TcpListener listener, string state, CancellationToken cancel)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancel);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var target = (await reader.ReadLineAsync(cancel))?.Split(' ') is [_, var path, ..] ? path : "";
            if (!target.StartsWith("/callback", StringComparison.Ordinal))
            {
                await RespondAsync(stream, 404, "");
                continue;
            }

            var query = (target.Split('?', 2) is [_, var q] ? q : "").Split('&')
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

            // Anything else on this PC could call this address; only Spotify knows the state.
            if (query.GetValueOrDefault("state") != state)
            {
                await RespondAsync(stream, 400, "Geçersiz istek.");
                continue;
            }
            if (query.GetValueOrDefault("code") is not { } code)
            {
                await RespondAsync(stream, 200, "Spotify bağlantısı onaylanmadı. Bu sekmeyi kapatabilirsin.");
                throw new OperationCanceledException();
            }
            await RespondAsync(stream, 200, "Spotify bağlandı. Bu sekmeyi kapatabilirsin.");
            return code;
        }
    }

    static async Task RespondAsync(Stream stream, int status, string message)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>Dynamic Island</title><body style=\"font:16px sans-serif;margin:40px\">{message}");
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
    }

    static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
