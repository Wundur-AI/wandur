using L = Wandur.Core.Localization.Strings;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wandur.Core.Diagnostics;

namespace Wandur.Core.Discovery;

/// <summary>A local snapshot searched in-process. Credentials stay on the directory server.</summary>
public sealed partial class WorldCatalog : IWorldDirectory, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly IWorldCatalogCache _cache;
    private readonly SemaphoreSlim _loadLock = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    /// <summary>A world opens with a snapshot at most this old; an older one is refreshed first, so a pack changed on
    /// the server since the last fetch is applied on open instead of five minutes later.</summary>
    public static readonly TimeSpan OpenRefreshAge = TimeSpan.FromSeconds(60);
    /// <summary>How long an open waits for that refresh before it goes ahead with the cached copy.</summary>
    public static readonly TimeSpan OpenRefreshTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _time;
    private long? _lastAttempt;
    private long? _lastLoaded;
    public IReadOnlyList<WorldListing> Worlds { get; private set; } = [];
    /// <summary>The snapshot's own timestamp, which describes the source listings, not when this process fetched it.</summary>
    public DateTimeOffset? FetchedAt { get; private set; }
    /// <summary>The clock the catalog runs on; views ask it what "now" is, so tests can hold it still.</summary>
    public TimeProvider Clock => _time;
    /// <summary>How long ago this process fetched the snapshot in use, or null when it came from the cache on disk.</summary>
    public TimeSpan? SnapshotAge => _lastLoaded is { } loaded ? _time.GetElapsedTime(loaded) : null;
    public string? Warning { get; private set; }
    public bool Loading { get; private set; }
    public Uri BaseUri { get; }
    public event Action? Changed;

    /// <summary>The install id header this catalog's own HttpClient sends to wandur.net; the window applies the settings
    /// to it. A catalog given an HttpClient by its caller sends nothing extra.</summary>
    public InstallHeader Install { get; }

    public WorldCatalog(string cachePath, Uri? baseUri = null, HttpClient? http = null, TimeProvider? timeProvider = null, InstallHeader? install = null)
        : this(new FileWorldCatalogCache(cachePath), baseUri, http, timeProvider, install) { }

    public WorldCatalog(IWorldCatalogCache cache, Uri? baseUri = null, HttpClient? http = null, TimeProvider? timeProvider = null, InstallHeader? install = null)
    {
        _cache = cache;
        Install = install ?? new InstallHeader();
        _time = timeProvider ?? TimeProvider.System;
        BaseUri = baseUri ?? new Uri((Environment.GetEnvironmentVariable("WANDUR_DIRECTORY_URL") ?? "https://api.wandur.net").TrimEnd('/') + "/");
        _ownsHttp = http is null;
        if (http is null)
        {
            // The catalog's own client: every directory, art and theme request carries the client's name and version,
            // and requests to wandur.net (not supplied banners on other hosts) carry the install id when it is on.
            http = new HttpClient(InstallIdentity.CreateHandler(BaseUri, Install)) { Timeout = TimeSpan.FromMinutes(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(ClientUserAgent.Value);
        }
        _http = http;
        try
        {
            if (_cache.ReadSnapshot() is { } json)
            {
                var snapshot = WorldDirectorySnapshot.Parse(json, out var legacy);
                (Worlds, FetchedAt) = (snapshot.Worlds, snapshot.FetchedAt);
                if (legacy)
                {
                    // Migration is optional when the cache directory is read-only. Keep the usable snapshot.
                    try { _cache.WriteSnapshot(snapshot.ToJson()); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or FormatException)
        { Warning = L.TheLocalDirectoryCacheCouldNotBeRead; }
    }

    public async Task LoadAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        await _loadLock.WaitAsync(token);
        try
        {
            // Provider timestamps describe source listings, not server-generated metadata.
            // Throttle local attempts (including failures), never the upstream snapshot age.
            if (!force && _lastAttempt is { } attempted && _time.GetElapsedTime(attempted) < RefreshInterval) return;
            _lastAttempt = _time.GetTimestamp();
            Loading = true; Warning = null; Changed?.Invoke();
            using var response = await _http.GetAsync(new Uri(BaseUri, "directory"), token);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(token);
                if (error.Contains("Set MUDVERSE_API_KEY")) throw new HttpRequestException(L.SetMUDVERSEAPIKEYInDirectoryServerEnvAnd);
                response.EnsureSuccessStatusCode();
            }
            var json = await response.Content.ReadAsStringAsync(token);
            var parsed = WorldDirectorySnapshot.Parse(json, out _);
            token.ThrowIfCancellationRequested();
            _cache.WriteSnapshot(parsed.ToJson());
            var previous = Worlds;
            await Task.Run(() => AdoptRenamedWorlds(previous, parsed.Worlds), token);
            (Worlds, FetchedAt) = (parsed.Worlds, parsed.FetchedAt);
            _lastLoaded = _time.GetTimestamp();
            if (response.Headers.TryGetValues("X-Wandur-Stale", out var values) && values.Contains("true"))
                Warning = L.ShowingTheSavedDirectoryWhileTheServerRefreshesIt;
            Wandur.Core.Diagnostics.ThemeTrace.Write("catalog.load",
                $"ok from {BaseUri}directory worlds={parsed.Worlds.Length} themed={parsed.Worlds.Count(w => w.Theme is not null)}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or ArgumentException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Warning = ex is HttpRequestException && ex.Message.StartsWith("Set MUDVERSE") ? ex.Message :
                L.Format(L.DirectoryUnavailableAtStartTheDirectoryServerSavedWorlds, BaseUri);
            Wandur.Core.Diagnostics.ThemeTrace.Write("catalog.load",
                $"FAILED from {BaseUri}directory: {ex.GetType().Name}: {ex.Message}; serving {Worlds.Count} cached worlds");
        }
        finally { Loading = false; _loadLock.Release(); Changed?.Invoke(); }
    }

    /// <summary>Refreshes before a listed world opens when the snapshot in use is older than <see cref="OpenRefreshAge"/>
    /// or came from disk. Bounded by <see cref="OpenRefreshTimeout"/>; a failure or timeout leaves the cached copy in
    /// use, exactly as before. A world the snapshot does not list opens without waiting: the periodic refresh will
    /// list it, and the client never delays a private world on the directory's account.</summary>
    public async Task RefreshBeforeOpenAsync(string host, int port, bool tls, CancellationToken cancellationToken = default)
    {
        if (FindEndpoint(host, port, tls) is null || SnapshotAge is { } age && age < OpenRefreshAge) return;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(OpenRefreshTimeout);
        try { await LoadAsync(force: true, bounded.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { /* The cached copy opens the world. */ }
    }

    /// <summary>Ids are the directory's opaque strings, so a snapshot may rename a world (the move from
    /// <c>mudverse:&lt;n&gt;</c> to slugs did that for every world). Anything this client keeps under an id is the
    /// artwork cache; a world that vanished by id but is listed at the same endpoint adopts the new id by
    /// copying its picture under the new key, so nothing is downloaded twice. A cache that cannot be read or
    /// written costs at most that copy; the snapshot itself is never held back.</summary>
    private void AdoptRenamedWorlds(IReadOnlyList<WorldListing> previous, IReadOnlyList<WorldListing> current)
    {
        if (previous.Count == 0 || current.Count == 0) return;
        var ids = current.Select(world => world.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var old in previous)
        {
            if (ids.Contains(old.Id) || old.WebOnly || old.Host.Length == 0 || (old.Port ?? old.TlsPort) is null) continue;
            var renamed = current.Where(world => !world.WebOnly && SameHost(world.Host, old.Host) && world.Port == old.Port && world.TlsPort == old.TlsPort).Take(2).ToArray();
            if (renamed.Length != 1) continue;
            var moved = old with { Id = renamed[0].Id };
            // Only the id moved: a picture regenerated for a changed description is still fetched afresh. The banner
            // and each generated size are re-keyed on their own.
            var keys = new List<(string From, string To)>();
            if (old.HasSuppliedArtwork && moved.SuppliedArtKey == renamed[0].SuppliedArtKey) keys.Add((old.SuppliedArtKey, renamed[0].SuppliedArtKey));
            if (moved.GeneratedArtKey == renamed[0].GeneratedArtKey)
                foreach (var size in new string?[] { null, RowSize, HeroSize, HeroFallbackSize })
                    keys.Add((old.GeneratedArtKeyFor(size), renamed[0].GeneratedArtKeyFor(size)));
            foreach (var (from, to) in keys.Where(k => k.From != k.To))
            {
                try
                {
                    if (_cache.ReadArtwork(to) is null && _cache.ReadArtwork(from) is { } art) _cache.WriteArtwork(to, art);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
    }
    private static bool SameHost(string left, string right) => string.Equals(left.TrimEnd('.'), right.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    public WorldListing? FindEndpoint(string host, int port, bool tls)
    {
        var matches = Worlds.Where(w => !w.WebOnly && SameHost(w.Host, host) && (tls ? w.TlsPort == port : w.Port == port)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public IReadOnlyList<WorldListing> Search(string? query)
    {
        var terms = Normalize(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var worlds = Worlds;
        if (terms.Length == 0) return worlds;
        var index = SearchIndex(worlds);
        var scored = new List<(WorldListing World, int Score)>();
        for (var i = 0; i < worlds.Count; i++)
            if (Score(index[i], terms) is var score and > 0) scored.Add((worlds[i], score));
        return scored.OrderByDescending(x => x.Score).ThenBy(x => x.World.Name).Select(x => x.World).ToArray();
    }
    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>A world's searchable text, normalized once per catalog instead of on every query.</summary>
    private sealed record SearchText(string Name, string Tags, string Host, string Details, string[] Words);
    // One reference, so a search on another thread sees either the old index or the new one, never half of each.
    private sealed record Indexed(IReadOnlyList<WorldListing> Worlds, SearchText[] Index);
    private Indexed? _searchIndex;
    private SearchText[] SearchIndex(IReadOnlyList<WorldListing> worlds)
    {
        if (_searchIndex is { } cached && ReferenceEquals(cached.Worlds, worlds)) return cached.Index;
        var index = new SearchText[worlds.Count];
        for (var i = 0; i < index.Length; i++)
        {
            var world = worlds[i];
            var name = Normalize(world.Name);
            index[i] = new(name, Normalize(world.SearchTags), Normalize($"{world.Host} {world.Port} {world.TlsPort}"),
                Normalize(world.Summary + " " + world.Description), name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        _searchIndex = new(worlds, index);
        return index;
    }
    private static int Score(SearchText world, string[] terms)
    {
        var (name, tags, host, details, words) = (world.Name, world.Tags, world.Host, world.Details, world.Words);
        var score = 0;
        foreach (var term in terms)
        {
            if (name == term) score += 100;
            else if (name.Contains(term)) score += 60;
            else if (host.Contains(term)) score += 50;
            else if (tags.Contains(term)) score += 35;
            else if (details.Contains(term)) score += 10;
            else if (term.Length >= 4 && words.Any(word => Distance(term, word) <= (term.Length >= 7 ? 2 : 1))) score += 20;
            else return 0;
        }
        return score;
    }
    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 3;
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0]; row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var old = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = old;
            }
        }
        return row[b.Length];
    }
    public async Task<WorldNameSuggestion?> LookupAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken: cancellationToken);
        if (Worlds.Count == 0 && Warning is not null) throw new HttpRequestException(Warning);
        var matches = Worlds.Where(w => string.Equals(w.Host.TrimEnd('.'), host.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) && (w.Port == port || w.TlsPort == port)).ToArray();
        return matches.Length == 1 && Uri.TryCreate(matches[0].Source.ListingUrl, UriKind.Absolute, out var uri) ? new(matches[0].Name, uri) : null;
    }
    public Task<byte[]?> GetArtAsync(WorldListing world, CancellationToken token = default) =>
        GetArtAsync(world, WorldArtwork.Preferred, null, token);

    public Task<byte[]?> GetArtAsync(WorldListing world, WorldArtwork kind, CancellationToken token = default) =>
        GetArtAsync(world, kind, null, token);

    /// <summary>Generated art sizes the directory serves: the site's 400 wide row plate, and the page hero.</summary>
    public const string RowSize = "400", HeroSize = "hero", HeroFallbackSize = "1024";

    /// <summary>
    /// One picture of a world. <see cref="WorldArtwork.Preferred"/> resolves through
    /// <see cref="WorldListing.PreferredArtwork"/>; the other two ask for one kind only and return null without it.
    /// <paramref name="size"/> asks the directory for a smaller generated picture (<c>?size=400</c>); each size is
    /// cached under its own key. A banner is fetched as it is.
    /// </summary>
    /// <remarks>Cancelling <paramref name="token"/> stops the wait, not the download: a fetch that has started runs to
    /// the end and is cached, so a row recycled mid-download finds the picture waiting when it comes back.</remarks>
    public async Task<byte[]?> GetArtAsync(WorldListing world, WorldArtwork kind, string? size, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (kind == WorldArtwork.Preferred) kind = world.PreferredArtwork;
        var supplied = kind == WorldArtwork.Supplied;
        if (supplied ? !world.HasSuppliedArtwork : !world.HasGeneratedArtwork) return null;
        var key = supplied ? world.SuppliedArtKey : world.GeneratedArtKeyFor(size);
        if (_cache.ReadArtwork(key) is { } cached) return cached;
        Uri artUri;
        if (supplied)
        {
            if (!Uri.TryCreate(world.BannerUrl, UriKind.Absolute, out var banner) || banner.Scheme is not ("http" or "https") || banner.UserInfo.Length > 0) return null;
            artUri = banner;
        }
        else
        {
            artUri = new Uri(BaseUri, world.GeneratedArtworkPath);
            if (!BaseUri.IsBaseOf(artUri)) return null;
            // The size goes on the resolved address, so the base check above has already passed on the path itself.
            if (size is { Length: > 0 }) artUri = new UriBuilder(artUri) { Query = "size=" + Uri.EscapeDataString(size) }.Uri;
        }
        Task<byte[]?> fetch;
        lock (_fetches)
        {
            if (!_fetches.TryGetValue(key, out fetch!))
            {
                _fetches[key] = fetch = FetchAsync(artUri, key);
                _ = fetch.ContinueWith(_ => { lock (_fetches) _fetches.Remove(key); }, TaskScheduler.Default);
            }
        }
        return await fetch.WaitAsync(token);
    }

    /// <summary>The world page's hero: the owner's banner when they set one, else the generated picture at the hero
    /// size, falling back to 1024 when the directory has no hero size yet (a 404 or something that is not an image).</summary>
    public async Task<(byte[]? Bytes, WorldArtwork Kind)> GetHeroArtAsync(WorldListing world, CancellationToken token = default)
    {
        var kind = world.PreferredArtwork;
        if (kind == WorldArtwork.Supplied) return (await GetArtAsync(world, WorldArtwork.Supplied, null, token), kind);
        var bytes = await GetArtAsync(world, WorldArtwork.Generated, HeroSize, token)
            ?? await GetArtAsync(world, WorldArtwork.Generated, HeroFallbackSize, token);
        return (bytes, kind);
    }

    private readonly Dictionary<string, Task<byte[]?>> _fetches = new(StringComparer.Ordinal);

    /// <summary>At most this many pictures download at once, whoever asks.</summary>
    public const int ArtDownloads = 4;
    private readonly SemaphoreSlim _downloads = new(ArtDownloads, ArtDownloads);

    private async Task<byte[]?> FetchAsync(Uri artUri, string key)
    {
        await Task.Yield();
        var url = artUri.AbsoluteUri;
        await _downloads.WaitAsync(_lifetime.Token);
        try
        {
            return await DownloadAsync(artUri, key, url);
        }
        finally { _downloads.Release(); }
    }

    private async Task<byte[]?> DownloadAsync(Uri artUri, string key, string url)
    {
        try
        {
            using var response = await _http.GetAsync(artUri, _lifetime.Token);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) { ArtTrace.Write(url, status, 0); return null; }
            var data = await response.Content.ReadAsByteArrayAsync(_lifetime.Token);
            if (!LooksLikeImage(response.Content.Headers.ContentType?.MediaType, data)) { ArtTrace.Write(url, status, data.Length, "not an image"); return null; }
            _cache.WriteArtwork(key, data);
            ArtTrace.Write(url, status, data.Length);
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            ArtTrace.Write(url, 0, 0, ex.GetType().Name + ": " + ex.Message);
            if (ex is TaskCanceledException && _lifetime.IsCancellationRequested) throw;
            return null;
        }
    }

    /// <summary>An answer that says it is something other than a picture (an HTML page served with 200, JSON, plain
    /// text), or that is empty, or that opens like markup, is not one. An untyped or image answer is taken as given;
    /// the view still falls back if it cannot be decoded.</summary>
    internal static bool LooksLikeImage(string? mediaType, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return false;
        if (mediaType is { Length: > 0 } type && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)) return false;
        var start = data.TrimStart(" \t\r\n"u8);
        return !start.IsEmpty && start[0] != (byte)'<' && start[0] != (byte)'{';
    }
    public void Dispose() { _lifetime.Cancel(); if (_ownsHttp) _http.Dispose(); }
}
