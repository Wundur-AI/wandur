using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.Views;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// The directory against wandur.net: rows and a world's page at a desktop and a narrow width, under a light and
/// dark themes. Set WANDUR_CAPTURE_DIR to keep the renders. The worlds, text and artwork are fictional and drawn here.
/// </summary>
[Collection(UiLanguageCollection.Name)]
public sealed class DirectorySiteLookCaptureTests
{
    [AvaloniaTheory]
    [InlineData("Hull", 1280)]
    [InlineData("Slate", 1280)]
    [InlineData("Ember", 1280)]
    [InlineData("Hull", 440)]
    [InlineData("Slate", 440)]
    public async Task ListAndWorldPageFollowTheSiteUnderTheTheme(string theme, int width)
    {
        await using var fixture = new Fixture(theme);
        var narrow = width < 600;
        var browser = new WorldBrowserView(fixture.Window.Sessions.Browser(fixture.Catalog), fixture.Catalog);
        var window = new Window { Content = browser, Width = width, Height = narrow ? 1100 : 940 };
        try
        {
            window.Show(); Layout(window);
            var list = Find<ListBox>(browser, "DirectoryResults");
            Assert.Equal(L.Format(L.WorldsToExplore, 4), Find<TextBlock>(browser, "DirectoryCount").Text);
            await WaitForArtwork(window, list);

            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.True(rows.Length >= 2);
            foreach (var row in rows)
            {
                var world = Assert.IsType<WorldListing>(row.DataContext);
                var card = Find<DirectoryWorldCard>(row);
                var plate = Find<Border>(row, "DirectoryResultIdentity");
                if (narrow) Assert.True(plate.Bounds.Width > card.Bounds.Width * .95, "A narrow row puts its plate on top, full width.");
                else
                {
                    Assert.InRange(plate.Bounds.Width, 180, DirectoryWorldCard.PlateWidth);
                    Assert.True(plate.Bounds.Height >= plate.Bounds.Width / DirectoryWorldCard.PlateAspect - 1, "The plate keeps at least the site's 5:2.");
                }
                var pills = Find<FlowPanel>(row, "DirectoryRowPills").Children.OfType<Border>()
                    .Select(p => ((TextBlock)p.Child!).Text).ToArray();
                Assert.Equal(world.Features.Theme, pills[0]);
                Assert.True(pills.Length <= 3);
                var live = row.GetVisualDescendants().OfType<StackPanel>().SingleOrDefault(p => p.Name == "DirectoryLive");
                if (!world.IsOnline) Assert.Null(live);
                else Assert.Equal(world.LivePlayerCount is { } n ? L.Format(L.OnlineCount, n) : L.StatusOnline,
                    live!.Children.OfType<TextBlock>().Single().Text);
                var beginner = row.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Name == "DirectoryBeginner");
                Assert.Equal(world.BeginnerFriendly == true, beginner is not null);
                Assert.True(Find<TextBlock>(row, "DirectoryRowBlurb").MaxLines == 2);
                Assert.Equal(L.ExploreWorld, Find<Button>(row, "DirectoryRowExplore").Content);
                foreach (var button in row.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), browser)!.Value;
                    Assert.InRange(end.X, 0, browser.Bounds.Width);
                }
                Assert.Empty(ContrastProbe.Scan(row));
            }
            // A count copied from another listing is never shown as live.
            var copied = Row(window, list, "emberwild");
            Assert.Equal(L.StatusOnline, Find<StackPanel>(copied, "DirectoryLive").Children.OfType<TextBlock>().Single().Text);
            Capture(window, $"directory-list-{theme.ToLowerInvariant()}-{width}.png");

            var starfall = Row(window, list, "starfall");
            Find<Button>(starfall, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Height = narrow ? 2600 : 1500; Layout(window);
            for (var i = 0; i < 100 && (Find<Image>(browser, "DirectoryArtwork").Source is null
                     || !browser.GetVisualDescendants().Any(v => v.Name == "DirectoryGlimpse")); i++)
            { await Task.Delay(10); Layout(window); }
            Assert.Equal("Starfall", Find<TextBlock>(browser, "DirectoryWorldTitle").Text);
            Assert.NotNull(Find<Image>(browser, "DirectoryArtwork").Source);
            Assert.Contains(browser.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == L.Format(L.AboutWorld, "Starfall"));
            Assert.Contains(browser.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == L.WorldDetails);
            Assert.Contains(browser.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == L.Format(L.PlayersOnlineCount, 142));
            Assert.Single(Find<StackPanel>(browser, "DirectoryWorldPage").GetVisualDescendants().OfType<Border>(), b => b.Name == "DirectoryBeginner");
            Assert.Equal(3, Find<Grid>(browser, "DirectoryFindYourPlace").Children.Count);
            Assert.True(Find<Button>(browser, "ConnectDirectoryWorld").IsEnabled);
            Assert.NotNull(Find<Border>(browser, "DirectoryGlimpse"));
            var about = Find<Control>(browser, "DirectoryWorldDescription");
            var details = Find<Border>(browser, "DirectoryWorldDetails");
            var aboutAt = about.TranslatePoint(default, browser)!.Value;
            var detailsAt = details.TranslatePoint(default, browser)!.Value;
            if (narrow) Assert.True(detailsAt.Y > aboutAt.Y, "A narrow page stacks World details under About.");
            else Assert.True(detailsAt.X > aboutAt.X && Math.Abs(details.Bounds.Width - 380) < 1, "World details sits beside About at the site's 380.");
            var toolbar = Find<Border>(browser, "DirectoryListingToolbar");
            var end2 = toolbar.TranslatePoint(new Point(toolbar.Bounds.Width, 0), browser)!.Value;
            Assert.InRange(end2.X, 0, browser.Bounds.Width);
            Assert.Empty(ContrastProbe.Scan(Find<Border>(browser, "DirectoryWorldDetails")));
            Capture(window, $"directory-world-{theme.ToLowerInvariant()}-{width}.png");
        }
        finally { window.Close(); }
    }

    private static ListBoxItem Row(Window window, ListBox list, string id)
    {
        list.ScrollIntoView(list.Items.OfType<WorldListing>().Single(w => w.Id == id)); Layout(window);
        return list.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing world && world.Id == id);
    }

    private static async Task WaitForArtwork(Window window, ListBox list)
    {
        for (var i = 0; i < 200; i++)
        {
            var images = list.GetVisualDescendants().OfType<Image>().Where(image => image.Name == "DirectoryRowArtwork").ToArray();
            if (images.Length > 0 && images.All(image => image.Source is not null)) return;
            await Task.Delay(10); Layout(window);
        }
    }

    private static T Find<T>(Visual root, string? name = null) where T : Control
    {
        var matches = root.GetVisualDescendants().OfType<T>().Where(c => name is null || c.Name == name).ToArray();
        Assert.True(matches.Length == 1, $"Expected one {typeof(T).Name} named {name}, found {matches.Length}.");
        return matches[0];
    }

    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); }

    private static void Capture(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        frame.Save(System.IO.Path.Combine(directory, name), new PngBitmapEncoderOptions());
    }

    /// <summary>A small landscape per world: sky, sun and two ridges, drawn here so the captures carry no real art.</summary>
    internal static byte[] Landscape(int width, int height, Color sky, Color ground, Color sun)
    {
        using var target = new RenderTargetBitmap(new PixelSize(width, height));
        using (var context = target.CreateDrawingContext())
        {
            context.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(sky, 0), new GradientStop(Color.FromRgb((byte)(sky.R / 3), (byte)(sky.G / 3), (byte)(sky.B / 3)), 1)]
            }, new Rect(0, 0, width, height));
            context.DrawEllipse(new SolidColorBrush(sun), null, new Point(width * .72, height * .32), height * .14, height * .14);
            var far = StreamGeometry.Parse($"M 0,{height * .7} L {width * .2},{height * .45} L {width * .38},{height * .62} L {width * .6},{height * .38} L {width * .82},{height * .6} L {width},{height * .5} L {width},{height} L 0,{height} Z");
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(200, ground.R, ground.G, ground.B)), null, far);
            var near = StreamGeometry.Parse($"M 0,{height * .85} C {width * .3},{height * .7} {width * .6},{height * .95} {width},{height * .78} L {width},{height} L 0,{height} Z");
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb((byte)(ground.R / 2), (byte)(ground.G / 2), (byte)(ground.B / 2))), null, near);
        }
        using var png = new MemoryStream();
        target.Save(png, new PngBitmapEncoderOptions());
        return png.ToArray();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wandur-directory-site-" + Guid.NewGuid());
        private readonly HttpClient _http;
        public WorldCatalog Catalog { get; }
        public MainWindow Window { get; }

        public Fixture(string theme)
        {
            Directory.CreateDirectory(_directory);
            var online = new WorldAvailability { Online = true };
            var worlds = new[]
            {
                new WorldListing
                {
                    Id = "starfall", Name = "Starfall", Host = "starfall.example.org", Port = 4000, TlsPort = 4443,
                    Summary = "A living galaxy. Your story to tell.",
                    Description = "Beyond the settled systems, the galaxy is still being written. Become a pilot, trader, explorer, or diplomat in a persistent world shaped by its players.\n\nChart forgotten routes, forge alliances, and make a home among the stars. Your choices become part of a shared history.",
                    BeginnerFriendly = true, Availability = online, EstablishedAt = new DateTimeOffset(2018, 5, 1, 0, 0, 0, TimeSpan.Zero),
                    Population = new() { LatestCount = 142, Source = "wandur", ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-20) },
                    Features = new() { Theme = "Science fiction", Kind = "MUD", Language = "English", Codebase = "Custom", Roleplaying = "Encouraged", PlayerKilling = "Restricted", WorldSize = "5000+", Location = "Canada", DevelopmentStatus = "Operational" },
                    Tags = ["Roleplay", "Exploration", "Trading"], GeneratedArtworkPath = "worlds/starfall/art", BannerUrl = "https://art.example.org/starfall-banner.png",
                    WebsiteUrl = "https://starfall.example.org", Source = new() { Name = "Test directory", ListingUrl = "https://listing.example.org/starfall" },
                    Community = new() { Rating = 4.6m, RatingCount = 12, Rank = 3, MonthlyVotes = 90 }
                },
                new WorldListing
                {
                    Id = "emberwild", Name = "Emberwild", Host = "emberwild.example.org", Port = 4000,
                    Summary = "Ancient forests, uneasy kingdoms, and a world changed by the people who call it home.",
                    Description = "Ancient forests and uneasy kingdoms.", Availability = online,
                    Population = new() { LatestCount = 86, Source = "mudverse" },
                    Features = new() { Theme = "Fantasy", Language = "English", Roleplaying = "Suggested" }, Tags = ["Fantasy", "Exploration", "Crafting", "Clans"],
                    GeneratedArtworkPath = "worlds/emberwild/art", Source = new() { Name = "Test directory" }
                },
                new WorldListing
                {
                    Id = "harbor", Name = "The Last Harbor", Host = "harbor.example.org", Port = 5000, BeginnerFriendly = true,
                    Summary = "A windswept port of secrets, sea voyages, and stories waiting beyond the shoreline.",
                    Availability = online, Population = new() { LatestCount = 53, Source = "wandur" },
                    Features = new() { Theme = "Adventure" }, Tags = ["Story-rich"], GeneratedArtworkPath = "worlds/harbor/art", Source = new() { Name = "Test directory" }
                },
                new WorldListing
                {
                    Id = "moss", Name = "Moss & Myth", Host = "moss.example.org", Port = 6000,
                    Summary = "Small adventures and lasting friendships in an ever-growing woodland world.",
                    Availability = new() { Online = false }, Features = new() { Theme = "Fantasy" }, Tags = ["Social", "Cozy", "Gardening"],
                    Source = new() { Name = "Test directory" }
                }
            };
            var snapshot = JsonSerializer.Serialize(new { format = "wandur.directory", schema_version = 2, fetched_at = DateTimeOffset.UtcNow, worlds },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var cache = System.IO.Path.Combine(_directory, "directory.json");
            File.WriteAllText(cache, snapshot);
            _http = new HttpClient(new ArtHandler(snapshot));
            Catalog = new WorldCatalog(cache, new Uri("https://directory.example.org/"), _http);
            var store = new SettingsStore(System.IO.Path.Combine(_directory, "settings.json"));
            store.Save(new ClientSettings { Theme = theme, UseWorldThemes = false });
            Window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
                new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: Catalog)
                { Width = 900, Height = 600 };
        }

        public async ValueTask DisposeAsync()
        {
            Window.Close();
            await Window.Sessions.DisposeAsync(); Catalog.Dispose(); _http.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class ArtHandler(string snapshot) : HttpMessageHandler
    {
        private static readonly Dictionary<string, (Color Sky, Color Ground, Color Sun)> Palettes = new()
        {
            ["starfall"] = (Color.Parse("#2B4C8C"), Color.Parse("#56607A"), Color.Parse("#F4E9C8")),
            ["emberwild"] = (Color.Parse("#C9814A"), Color.Parse("#2F5A3A"), Color.Parse("#FFE2A6")),
            ["harbor"] = (Color.Parse("#7A5A7E"), Color.Parse("#34465A"), Color.Parse("#FFC98A")),
            ["starfall-banner"] = (Color.Parse("#1B2A4A"), Color.Parse("#8A6A3A"), Color.Parse("#9FD4FF"))
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/directory") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(snapshot) });
            var key = path.StartsWith("/worlds/", StringComparison.Ordinal) ? path.Split('/')[2] : System.IO.Path.GetFileNameWithoutExtension(path);
            if (!Palettes.TryGetValue(key, out var palette)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var (width, height) = key.EndsWith("banner", StringComparison.Ordinal) ? (960, 540) : (1024, 683);
            byte[] bytes = [];
            Dispatcher.UIThread.Invoke(() => bytes = Landscape(width, height, palette.Sky, palette.Ground, palette.Sun));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
