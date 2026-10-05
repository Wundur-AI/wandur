using System.Net;
using System.Net.Sockets;
using System.Text;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;
using Wandur.Core.Updates;

namespace Wandur.Core.Tests;

public sealed class UpdateServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0.1.10", "0.1.9")]
    [InlineData("0.2.0", "0.1.99")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.1.5", "0.1.5-rc.2")]
    [InlineData("0.1.5-rc.10", "0.1.5-rc.2")]
    [InlineData("0.1.5-rc.2", "0.1.4")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    [InlineData("1.0.0-beta", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.11")]
    [InlineData("0.1.5", "0.0.0-dev")]
    public void VersionsCompareBySemanticVersionRules(string newer, string older)
    {
        Assert.True(ReleaseVersion.TryParse(newer, out var a));
        Assert.True(ReleaseVersion.TryParse(older, out var b));
        Assert.True(a!.CompareTo(b) > 0, $"{newer} > {older}");
        Assert.True(b!.CompareTo(a) < 0, $"{older} < {newer}");
    }

    [Theory]
    [InlineData("v0.1.5", "0.1.5")]
    [InlineData("0.1.5+abc123", "0.1.5")]
    [InlineData(" 0.1.6-rc.1 ", "0.1.6-rc.1")]
    public void PrefixesBuildMetadataAndSpacesAreIgnored(string text, string expected)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(expected, version!.ToString());
        Assert.True(ReleaseVersion.TryParse(expected, out var plain));
        Assert.Equal(0, version.CompareTo(plain));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.1")]
    [InlineData("0.1.x")]
    [InlineData("0.1.5-")]
    [InlineData("0.1.5 beta")]
    [InlineData("99999999999.0.0")]
    public void UnreadableVersionsAreRefused(string? text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public async Task ABuildFromSourceNeverChecksAndIsNeverOfferedAnything()
    {
        var source = new FakeSource("0.1.5");
        var dev = new UpdateService(source, new Clock(Start), "0.0.0-dev");
        var settings = new ClientSettings { LastUpdateCheck = new UpdateCheckRecord { CheckedAt = Start.AddDays(-3), Version = "0.1.5" } };

        Assert.True(ReleaseVersion.TryParse("0.0.0-dev", out var version) && version!.IsDevelopment);
        Assert.False(dev.ChecksAllowed);
        Assert.False(dev.IsDue(new ClientSettings()));
        Assert.False(dev.IsNewer("0.1.5"));
        Assert.Null(dev.Offer(settings));
        var result = await dev.CheckAsync(settings);
        Assert.Equal(UpdateCheckStatus.Disabled, result.Status);
        Assert.Null(result.Record);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void TheOwnerCanLetABuildFromSourceCheckAsARelease()
    {
        Assert.Equal("0.1.4", UpdateService.EffectiveVersion("0.0.0-dev", "0.1.4"));
        Assert.Equal("0.1.4", UpdateService.EffectiveVersion("0.0.0-dev", "v0.1.4"));
        Assert.Equal("0.0.0-dev", UpdateService.EffectiveVersion("0.0.0-dev", null));
        Assert.Equal("0.0.0-dev", UpdateService.EffectiveVersion("0.0.0-dev", "nonsense"));
        Assert.Equal("0.0.0-dev", UpdateService.EffectiveVersion("0.0.0-dev", "0.0.0-dev"));
        // A release build is what it is; the override only applies to a build from source.
        Assert.Equal("0.1.5", UpdateService.EffectiveVersion("0.1.5", "0.1.1"));
        // This test assembly is built from source.
        Assert.Equal("0.0.0-dev", UpdateService.BuildVersion());
    }

    [Fact]
    public async Task ItAsksAtMostOnceADayAndARestartWithinTheDayDoesNotAskAgain()
    {
        var clock = new Clock(Start);
        var source = new FakeSource("0.1.6");
        var updates = new UpdateService(source, clock, "0.1.5");
        var settings = new ClientSettings();

        Assert.True(updates.IsDue(settings));   // Never checked.
        var result = await updates.CheckAsync(settings);
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal(Start, result.Record!.CheckedAt);
        settings = settings with { LastUpdateCheck = result.Record };

        // The same settings after a restart: nothing is asked again within the day, and the result is remembered.
        var restarted = new UpdateService(source, clock, "0.1.5");
        clock.Now = Start.AddHours(23).AddMinutes(59);
        Assert.False(restarted.IsDue(settings));
        Assert.Equal("0.1.6", restarted.Offer(settings)!.Version);
        clock.Now = Start.AddDays(1);
        Assert.True(restarted.IsDue(settings));

        // A clock set back far behind the last check does not stall checks forever.
        clock.Now = Start.AddDays(-2);
        Assert.True(restarted.IsDue(settings));

        // Turned off in Settings: never due, and nothing remembered is offered.
        clock.Now = Start.AddDays(5);
        Assert.False(restarted.IsDue(settings with { CheckForUpdates = false }));
        Assert.Null(restarted.Offer(settings with { CheckForUpdates = false }));
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task AFailureIsReportedOnceKeepsWhatWasKnownAndWaitsADay()
    {
        var clock = new Clock(Start);
        var source = new FakeSource("0.1.6");
        var updates = new UpdateService(source, clock, "0.1.5");
        var settings = new ClientSettings { LastUpdateCheck = (await updates.CheckAsync(new ClientSettings())).Record };

        clock.Now = Start.AddDays(1);
        source.Fail = true;
        var failed = await updates.CheckAsync(settings);
        Assert.Equal(UpdateCheckStatus.Failed, failed.Status);
        Assert.Equal(2, source.Calls);   // One attempt, no retries.
        Assert.Equal(Start.AddDays(1), failed.Record!.CheckedAt);
        Assert.Equal("0.1.6", failed.Record.Version);
        settings = settings with { LastUpdateCheck = failed.Record };
        Assert.False(updates.IsDue(settings));
        Assert.Equal("0.1.6", updates.Offer(settings)!.Version);
    }

    [Fact]
    public async Task OnlyANewerReleaseIsOffered()
    {
        var clock = new Clock(Start);
        foreach (var (running, latest, status) in new[]
        {
            ("0.1.5", "0.1.5", UpdateCheckStatus.UpToDate),
            ("0.1.6", "0.1.5", UpdateCheckStatus.UpToDate),
            ("0.1.5-rc.2", "0.1.5", UpdateCheckStatus.Available),
            ("0.1.9", "0.1.10", UpdateCheckStatus.Available),
        })
        {
            var updates = new UpdateService(new FakeSource(latest), clock, running);
            var result = await updates.CheckAsync(new ClientSettings());
            Assert.Equal(status, result.Status);
            var offer = updates.Offer(new ClientSettings { LastUpdateCheck = result.Record });
            Assert.Equal(status == UpdateCheckStatus.Available, offer is not null);
        }
    }

    [Fact]
    public void ASkippedVersionStaysHiddenAndANewerOneShowsAgain()
    {
        var updates = new UpdateService(new FakeSource("0.1.6"), new Clock(Start), "0.1.5");
        var record = new UpdateCheckRecord { CheckedAt = Start, Version = "0.1.6", Page = "https://www.wandur.net/client/downloads" };
        var settings = new ClientSettings { LastUpdateCheck = record, SkippedUpdateVersion = "0.1.6" };

        Assert.Null(updates.Offer(settings));
        Assert.Null(updates.Offer(settings with { SkippedUpdateVersion = "v0.1.6" }));
        var newer = settings with { LastUpdateCheck = record with { Version = "0.1.7" } };
        Assert.Equal("0.1.7", updates.Offer(newer)!.Version);
    }

    [Fact]
    public void OnlyPagesOnWandurAndNotesOnGitHubAreOpened()
    {
        var info = HttpUpdateSource.Parse("""{"version":"0.1.6","page":"https://evil.example/download","notes":"http://github.com/x"}""");
        Assert.Equal(UpdateService.DownloadsPage, info.Page);
        Assert.Null(info.Notes);
        info = HttpUpdateSource.Parse("""{"version":"0.1.6","page":"https://www.wandur.net/client/downloads","notes":"https://github.com/Last-Mile-Studio/wandur/releases/tag/v0.1.6"}""");
        Assert.Equal("https://www.wandur.net/client/downloads", info.Page.AbsoluteUri);
        Assert.Equal("https://github.com/Last-Mile-Studio/wandur/releases/tag/v0.1.6", info.Notes!.AbsoluteUri);
        Assert.Null(UpdateService.TrustedPage("https://user@www.wandur.net/"));
        Assert.Null(UpdateService.TrustedPage("https://wandur.net.evil.example/"));
        Assert.Throws<FormatException>(() => HttpUpdateSource.Parse("""{"version":"0.0.0-dev"}"""));
    }

    [Fact]
    public async Task TheCheckAsksTheDirectoryHostWithTheClientsUserAgent()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var body = """
            { "version": "0.1.6", "published_at": "2026-10-03T12:48:44Z",
              "page": "https://www.wandur.net/client/downloads",
              "notes": "https://github.com/Last-Mile-Studio/wandur/releases/tag/v0.1.6",
              "files": [ { "platform": "macos-arm64", "name": "Wandur-0.1.6-macos-arm64.dmg", "url": "https://github.com/x.dmg", "size": 1, "sha256": null } ] }
            """;
        var serve = Serve(listener, "200 OK", body);

        using var source = new HttpUpdateSource(new Uri($"http://127.0.0.1:{port}"));
        var updates = new UpdateService(source, new Clock(Start), "0.1.5");
        var result = await updates.CheckAsync(new ClientSettings());

        var request = await serve.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.StartsWith("GET /client/latest ", request);
        Assert.Contains($"User-Agent: {ClientUserAgent.Value}\r\n", request);
        Assert.Contains("Accept: application/json\r\n", request);
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal("0.1.6", result.Latest!.Version);
        Assert.Equal("https://github.com/Last-Mile-Studio/wandur/releases/tag/v0.1.6", result.Record!.Notes);

        // The site has no list yet: a 503, reported as a failure.
        var unavailable = Serve(listener, "503 Service Unavailable", """{"error":"later"}""");
        Assert.Equal(UpdateCheckStatus.Failed, (await updates.CheckAsync(new ClientSettings())).Status);
        await unavailable.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task TestsNeverReachTheRealSite()
    {
        // The default address is the directory's, which the test projects point at a closed loopback port.
        using var catalog = new WorldCatalog(Path.Combine(Path.GetTempPath(), "wandur-update-" + Guid.NewGuid(), "directory.json"));
        using var source = new HttpUpdateSource(catalog.BaseUri);
        Assert.Equal(new Uri(new Uri(OfflineDirectory.Address), "client/latest"), source.Address);
        var result = await new UpdateService(source, new Clock(Start), "0.1.5").CheckAsync(new ClientSettings());
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    private static Task<string> Serve(TcpListener listener, string status, string body) => Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream();
        var head = new StringBuilder();
        var buffer = new byte[4096];
        while (!head.ToString().Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        var bytes = Encoding.UTF8.GetBytes(body);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
        await stream.WriteAsync(bytes);
        return head.ToString();
    });

    internal sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class FakeSource(string version) : IUpdateSource
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<UpdateInfo> GetLatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("offline");
            return Task.FromResult(new UpdateInfo(version, UpdateService.DownloadsPage, new Uri($"https://github.com/Last-Mile-Studio/wandur/releases/tag/v{version}")));
        }
    }
}
