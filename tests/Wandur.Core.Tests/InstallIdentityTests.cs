using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;
using Wandur.Core.Storage;
using Wandur.Core.Updates;

namespace Wandur.Core.Tests;

public sealed class InstallIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wandur-install-" + Guid.NewGuid());

    private SettingsLoadResult LoadFresh(string folder)
    {
        using var database = new ClientDatabase(Path.Combine(_dir, folder, "wandur.db"));
        return new SqliteSettingsStore(database, Path.Combine(_dir, folder, "settings.json")).Load();
    }

    private void SaveFresh(string folder, ClientSettings settings)
    {
        using var database = new ClientDatabase(Path.Combine(_dir, folder, "wandur.db"));
        new SqliteSettingsStore(database, Path.Combine(_dir, folder, "settings.json")).Save(settings);
    }

    [Fact]
    public void TheIdIsMadeOnceAndSurvivesRestartsAndSettingsUpdates()
    {
        var first = LoadFresh("a").Settings;
        Assert.NotNull(first.InstallId);
        Assert.NotEqual(Guid.Empty, first.InstallId);
        Assert.True(first.SendInstallId);
        var id = first.InstallId;

        // A restart opens the database again and finds the same id.
        Assert.Equal(id, LoadFresh("a").Settings.InstallId);

        // Saving changed settings keeps it, and so does a copy made before the id existed or one carrying another id.
        SaveFresh("a", first with { Theme = "Ember", FontSize = 17 });
        SaveFresh("a", new ClientSettings { Theme = "Ember" });
        SaveFresh("a", first with { Theme = "Ember", InstallId = Guid.NewGuid() });
        var reloaded = LoadFresh("a").Settings;
        Assert.Equal(id, reloaded.InstallId);
        Assert.Equal("Ember", reloaded.Theme);

        // Turning sending off is a setting like any other and leaves the id alone.
        SaveFresh("a", reloaded with { SendInstallId = false });
        var off = LoadFresh("a").Settings;
        Assert.False(off.SendInstallId);
        Assert.Equal(id, off.InstallId);

        // Other settings, deleted or never made, get their own id.
        Assert.NotEqual(id, LoadFresh("b").Settings.InstallId);
    }

    [Fact]
    public void TheIdComesAlongWhenSettingsMoveFromTheOldFileToTheDatabase()
    {
        var folder = Path.Combine(_dir, "legacy");
        var file = new SettingsStore(Path.Combine(folder, "settings.json"));
        file.Save(new ClientSettings { Theme = "Ember" });
        var fileId = file.Load().Settings.InstallId;
        Assert.NotNull(fileId);
        file.Save(new ClientSettings { Theme = "Hull" });
        Assert.Equal(fileId, file.Load().Settings.InstallId);

        Assert.Equal(fileId, LoadFresh("legacy").Settings.InstallId);
    }

    [Theory]
    [InlineData("https://api.wandur.net/directory", true)]
    [InlineData("https://www.wandur.net/client/downloads", true)]
    [InlineData("https://wandur.net/", true)]
    [InlineData("http://api.wandur.net/directory", false)]
    [InlineData("https://api.wandur.net:8443/directory", false)]
    [InlineData("https://evilwandur.net/", false)]
    [InlineData("https://wandur.net.example.com/", false)]
    [InlineData("https://user@api.wandur.net/", false)]
    [InlineData("https://www.mudconnector.com/mud/x", false)]
    [InlineData("https://api.openai.com/v1/chat/completions", false)]
    [InlineData("http://127.0.0.1:5298/directory", true)]
    [InlineData("http://127.0.0.1:5299/directory", false)]
    [InlineData("http://localhost:5298/directory", false)]
    public void OnlyWandurNetAndTheConfiguredDirectoryCount(string address, bool expected) =>
        Assert.Equal(expected, InstallIdentity.IsWandurNet(new Uri(address), new Uri("http://127.0.0.1:5298/")));

    [Theory]
    [InlineData("https://api.wandur.net/directory", true)]
    [InlineData("https://api.wandur.net/directory/", true)]
    [InlineData("https://api.wandur.net/directory?since=1", true)]
    [InlineData("https://api.wandur.net/client/latest", true)]
    [InlineData("http://127.0.0.1:5298/base/directory", true)]
    [InlineData("https://api.wandur.net/", false)]
    [InlineData("https://api.wandur.net/art/world-1.png", false)]
    [InlineData("https://api.wandur.net/themes/ember.png", false)]
    [InlineData("https://api.wandur.net/directory/world-1", false)]
    [InlineData("https://api.wandur.net/client/downloads", false)]
    public void OnlyTheWorldListAndTheUpdateCheckCarryTheId(string address, bool expected) =>
        Assert.Equal(expected, InstallIdentity.IsCounted(new Uri(address)));

    [Fact]
    public async Task ArtworkAndOtherRequestsToTheSiteCarryNoId()
    {
        using var site = new LoopbackServer(_ => "HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 4\r\nConnection: close\r\n\r\nPNG!");
        var install = new InstallHeader();
        install.Apply(new ClientSettings { InstallId = Guid.NewGuid() });
        using var http = new HttpClient(InstallIdentity.CreateHandler(site.Base, install));

        Assert.Equal("PNG!", await http.GetStringAsync(new Uri(site.Base, "art/world-1.png")));
        var request = await site.NextAsync();
        Assert.StartsWith("GET /art/world-1.png ", request);
        Assert.DoesNotContain(InstallIdentity.Header, request, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheHeaderValueFollowsTheSetting()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id.ToString("D"), InstallIdentity.HeaderValue(new ClientSettings { InstallId = id }));
        Assert.Null(InstallIdentity.HeaderValue(new ClientSettings { InstallId = id, SendInstallId = false }));
        Assert.Null(InstallIdentity.HeaderValue(new ClientSettings()));
    }

    [Fact]
    public async Task TheCatalogSendsTheIdToTheDirectoryOnlyAndNeverInTheUserAgent()
    {
        using var site = new LoopbackServer(path => path switch
        {
            "/directory" => Json(JsonSerializer.Serialize(new { schema_version = 1, fetched_at = DateTimeOffset.UtcNow, games = Array.Empty<object>() })),
            _ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
        });
        var id = Guid.NewGuid();
        var install = new InstallHeader();
        install.Apply(new ClientSettings { InstallId = id });
        using var catalog = new WorldCatalog(Path.Combine(_dir, "directory.json"), site.Base, install: install);

        await catalog.LoadAsync(force: true);
        var request = await site.NextAsync();
        Assert.StartsWith("GET /directory ", request);
        Assert.Contains($"\r\n{InstallIdentity.Header}: {id:D}\r\n", request);
        Assert.Contains($"\r\nUser-Agent: {ClientUserAgent.Value}\r\n", request);
        Assert.DoesNotContain(id.ToString("D"), Line(request, "User-Agent"));

        // Off in the settings: the next request carries nothing.
        install.Apply(new ClientSettings { InstallId = id, SendInstallId = false });
        await catalog.LoadAsync(force: true);
        var off = await site.NextAsync();
        Assert.StartsWith("GET /directory ", off);
        Assert.DoesNotContain(InstallIdentity.Header, off, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id.ToString("D"), off);
    }

    [Fact]
    public async Task OtherHostsNeverSeeTheIdEvenThroughARedirect()
    {
        using var other = new LoopbackServer(_ => "HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 4\r\nConnection: close\r\n\r\nPNG!");
        using var site = new LoopbackServer(path => path == "/directory"
            ? $"HTTP/1.1 302 Found\r\nLocation: {other.Base}banner.png\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
            : "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        var id = Guid.NewGuid();
        var install = new InstallHeader();
        install.Apply(new ClientSettings { InstallId = id });
        using var http = new HttpClient(InstallIdentity.CreateHandler(site.Base, install));

        // A banner on another host, asked for directly: no id.
        Assert.Equal("PNG!", await http.GetStringAsync(new Uri(other.Base, "banner.png")));
        Assert.DoesNotContain(InstallIdentity.Header, await other.NextAsync(), StringComparison.OrdinalIgnoreCase);

        // The site redirects to the other host: the site sees the id, the other host does not.
        Assert.Equal("PNG!", await http.GetStringAsync(new Uri(site.Base, "directory")));
        Assert.Contains($"{InstallIdentity.Header}: {id:D}", await site.NextAsync());
        var redirected = await other.NextAsync();
        Assert.StartsWith("GET /banner.png ", redirected);
        Assert.DoesNotContain(InstallIdentity.Header, redirected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id.ToString("D"), redirected);
    }

    [Fact]
    public async Task TheUpdateCheckSendsTheIdWhenItIsOn()
    {
        using var site = new LoopbackServer(_ => Json("""{ "version": "0.1.6" }"""));
        var id = Guid.NewGuid();
        var install = new InstallHeader();
        install.Apply(new ClientSettings { InstallId = id });
        using var source = new HttpUpdateSource(site.Base, install: install);
        Assert.Equal("0.1.6", (await source.GetLatestAsync(CancellationToken.None)).Version);
        var request = await site.NextAsync();
        Assert.StartsWith("GET /client/latest ", request);
        Assert.Contains($"\r\n{InstallIdentity.Header}: {id:D}\r\n", request);

        install.Apply(new ClientSettings { InstallId = id, SendInstallId = false });
        await source.GetLatestAsync(CancellationToken.None);
        Assert.DoesNotContain(InstallIdentity.Header, await site.NextAsync(), StringComparison.OrdinalIgnoreCase);

        // Without an install header at all, as before.
        using var plain = new HttpUpdateSource(site.Base);
        await plain.GetLatestAsync(CancellationToken.None);
        Assert.DoesNotContain(InstallIdentity.Header, await site.NextAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Json(string body)
    {
        var bytes = Encoding.UTF8.GetByteCount(body);
        return $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes}\r\nConnection: close\r\n\r\n{body}";
    }

    private static string Line(string request, string name) =>
        request.Split("\r\n").FirstOrDefault(line => line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) ?? "";

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A plain HTTP/1.1 server on a loopback port that records each request's head and answers one per connection.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly BlockingCollection<string> _requests = new();
        private readonly CancellationTokenSource _stop = new();
        public Uri Base { get; }

        public LoopbackServer(Func<string, string> respond)
        {
            _listener.Start();
            Base = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (Exception) { return; }
                    using (client)
                    {
                        var stream = client.GetStream();
                        var head = new StringBuilder();
                        var buffer = new byte[4096];
                        while (!head.ToString().Contains("\r\n\r\n"))
                        {
                            var read = await stream.ReadAsync(buffer);
                            if (read == 0) break;
                            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        }
                        var text = head.ToString();
                        var path = text.Split(' ') is { Length: > 1 } parts ? parts[1] : "/";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(respond(path)));
                        _requests.Add(text);
                    }
                }
            });
        }

        public Task<string> NextAsync() => Task.Run(() =>
            _requests.TryTake(out var request, TimeSpan.FromSeconds(30)) ? request : throw new TimeoutException("No request arrived."));

        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
