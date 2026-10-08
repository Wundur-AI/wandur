using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;
using Wandur.Desktop.ViewModels;
using Wandur.Desktop.Views;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>What the rows and the page ask the directory for, and what they show when it has nothing.</summary>
[Collection(UiLanguageCollection.Name)]
public sealed class DirectoryArtLoadingTests
{
    [AvaloniaFact]
    public async Task RowsAskForTheRowSizeAndAMissingPictureLeavesTheInitialsPlate()
    {
        var found = new WorldListing { Id = "found", Name = "Found World", Host = "found.example.org", Port = 4000, GeneratedArtworkPath = "worlds/found/art" };
        var missing = new WorldListing { Id = "missing", Name = "Missing Picture", Host = "missing.example.org", Port = 4000, GeneratedArtworkPath = "worlds/missing/art" };
        await using var fixture = new Fixture([found, missing], request => request.RequestUri!.AbsolutePath.Contains("missing")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestPng.Rgba(40, 16)) });
        var window = new Window { Content = new WorldBrowserView(fixture.Model, fixture.Catalog), Width = 1280, Height = 900 };
        try
        {
            window.Show();
            Image Picture(string id) => window.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing w && w.Id == id)
                .GetVisualDescendants().OfType<Image>().Single(i => i.Name == "DirectoryRowArtwork");
            for (var i = 0; i < 100 && (Picture("found").Source is null || fixture.Requests.Count < 2); i++) { await Task.Delay(10); Layout(window); }
            Assert.NotNull(Picture("found").Source);
            Assert.Contains("https://directory.example.org/worlds/found/art?size=400", fixture.Requests);
            Assert.Contains("https://directory.example.org/worlds/missing/art?size=400", fixture.Requests);
            // The 404 leaves the plate with its initials, on the plate's own surface.
            var row = window.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing { Id: "missing" });
            Assert.Null(Picture("missing").Source);
            var plate = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DirectoryResultIdentity");
            Assert.Contains(plate.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "MP" && t.IsEffectivelyVisible);
            Assert.Equal(Application.Current!.Resources["DirectoryPlateBrush"], plate.Background);
            Assert.NotEqual(Application.Current!.Resources["ShellBrush"], plate.Background);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task APageShownAgainReadsTheCountAfresh()
    {
        var now = DateTimeOffset.Parse("2026-09-26T12:00:00Z");
        var clock = new MovingClock(now);
        var world = new WorldListing { Id = "starfall", Name = "Starfall", Host = "starfall.example.org", Port = 4000, Availability = new() { Online = true },
            Population = new() { LatestCount = 142, Source = "wandur", ObservedAt = now.AddMinutes(-30) } };
        await using var fixture = new Fixture([world], _ => new HttpResponseMessage(HttpStatusCode.NotFound), clock);
        var browser = new WorldBrowserView(fixture.Model, fixture.Catalog);
        var window = new Window { Content = browser, Width = 1280, Height = 1400 };
        try
        {
            window.Show(); Layout(window);
            string[] Text() => browser.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            void Explore()
            {
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Layout(window);
            }
            Explore();
            Assert.Contains(L.Format(L.PlayersOnlineCount, 142), Text());
            browser.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DirectoryBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            clock.Now = now.AddHours(3);
            Explore();
            Assert.DoesNotContain(L.Format(L.PlayersOnlineCount, 142), Text());
            Assert.Contains(L.Format(L.CountedAgo, "Wandur", 142, L.Format(L.HoursAgo, 3)), Text());
        }
        finally { window.Close(); }
    }

    /// <summary>Hundreds of listings: only the rows on screen exist, only they ask for artwork, and a row scrolled away
    /// lets go of its picture.</summary>
    [AvaloniaFact]
    public async Task OnlyTheRowsOnScreenAreBuiltAndAskForArtwork()
    {
        var worlds = Enumerable.Range(0, 300).Select(i => new WorldListing
        {
            Id = $"world-{i}", Name = $"World {i:D3}", Host = $"w{i}.example.org", Port = 4000, GeneratedArtworkPath = $"worlds/world-{i}/art",
            Community = new() { Rank = i + 1 }
        }).ToArray();
        await using var fixture = new Fixture(worlds, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestPng.Rgba(40, 16)) });
        var window = new Window { Content = new WorldBrowserView(fixture.Model, fixture.Catalog), Width = 1280, Height = 900 };
        try
        {
            window.Show();
            for (var i = 0; i < 50; i++) { await Task.Delay(10); Layout(window); }
            int Realized() => window.GetVisualDescendants().OfType<DirectoryWorldCard>().Count();
            int Requested() { lock (fixture.Requests) return fixture.Requests.Count; }
            var first = Realized();
            Assert.InRange(first, 1, 12);
            Assert.InRange(Requested(), 1, first);
            // The selected row (the first) may stay realized for keyboard focus; the second must not.
            var secondRow = window.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing { Id: "world-1" });
            var scroll = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DirectoryResults")
                .GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            for (var i = 0; i < 50; i++) { await Task.Delay(10); Layout(window); }
            Assert.InRange(Realized(), 1, 12);
            Assert.Contains(fixture.Requests, r => r.Contains("world-299", StringComparison.Ordinal));
            // Far fewer pictures than listings were ever asked for: the rows in between were never built.
            Assert.InRange(Requested(), 2, 30);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ListBoxItem>(), r => r.IsVisible && r.DataContext is WorldListing { Id: "world-1" });
            Assert.False(secondRow.IsVisible && secondRow.DataContext is WorldListing { Id: "world-1" });
        }
        finally { window.Close(); }
    }

    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); }

    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "wandur-directory-art-" + Guid.NewGuid());
        private readonly HttpClient _http;
        public List<string> Requests { get; } = [];
        public WorldCatalog Catalog { get; }
        public SessionWorkspace Sessions { get; }
        public WorldBrowserViewModel Model { get; }

        public Fixture(WorldListing[] worlds, Func<HttpRequestMessage, HttpResponseMessage> art, TimeProvider? clock = null)
        {
            Directory.CreateDirectory(_directory);
            var cache = Path.Combine(_directory, "directory.json");
            File.WriteAllText(cache, JsonSerializer.Serialize(new { format = "wandur.directory", schema_version = 2, fetched_at = DateTimeOffset.UtcNow, worlds },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            _http = new HttpClient(new Handler(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/directory") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                lock (Requests) Requests.Add(request.RequestUri.AbsoluteUri);
                return art(request);
            }));
            Catalog = new WorldCatalog(cache, new Uri("https://directory.example.org/"), _http, clock);
            var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
            store.Save(new ClientSettings { UseWorldThemes = false });
            Sessions = new SessionWorkspace(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
                new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: Catalog);
            Model = new WorldBrowserViewModel(Catalog, Sessions);
        }

        public async ValueTask DisposeAsync()
        {
            Model.Dispose(); await Sessions.DisposeAsync(); Catalog.Dispose(); _http.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
