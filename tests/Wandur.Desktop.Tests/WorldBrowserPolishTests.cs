using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
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

[Collection(UiLanguageCollection.Name)]
public sealed class WorldBrowserPolishTests
{
    [AvaloniaFact]
    public async Task KeyboardExploreAndBackPreserveFilteredScrollAndTheLiveSession()
    {
        await using var fixture = new Fixture("Slate", 1040, 680);
        var window = fixture.Window;
        try
        {
            window.Show();
            await window.Sessions.OpenAsync();
            var active = window.Controller;
            window.Sessions.Browse(); Layout(window);
            var browser = Find<WorldBrowserView>(window);
            var search = Find<TextBox>(browser, "DirectorySearch");
            search.Text = "Valley"; Layout(window);
            var list = Find<ListBox>(browser, "DirectoryResults");
            list.SelectedIndex = 3; list.ScrollIntoView(list.SelectedItem!); Layout(window);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var offset = scroll.Offset.Y;
            Assert.True(offset > 0, "offset");
            Assert.True(list.ContainerFromIndex(list.SelectedIndex)!.Focus(), "The selected directory row must accept keyboard focus.");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Layout(window);
            Assert.False(list.IsEffectivelyVisible);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); Layout(window);
            Assert.True(list.IsEffectivelyVisible, "back after escape");
            Assert.Equal("Valley", search.Text);
            Assert.Equal(3, list.SelectedIndex);
            Assert.InRange(Math.Abs(scroll.Offset.Y - offset), 0, 1);
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.IsSelected);
            Assert.True(row.IsFocused, "Back should return keyboard focus to the selected result.");
            Find<Button>(row, "DirectoryRowSave").Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Layout(window);
            Assert.Single(window.Controller.Settings.Profiles);
            Assert.True(list.IsEffectivelyVisible, "after save");
            Assert.Same(active, window.Controller);
            Assert.True(active.IsConnected, "connected");
            Assert.Single(window.Sessions.Tabs);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ADetachedRowsLateArtworkCannotReplaceTheReattachedPicture()
    {
        var oldLoad = new TaskCompletionSource<Bitmap?>();
        var newLoad = new TaskCompletionSource<Bitmap?>();
        var tokens = new List<CancellationToken>();
        var world = new WorldListing { Id = "harbor", Name = "Harbor", Host = "harbor.example", Port = 4000 };
        var card = new DirectoryWorldCard(world, _ => { }, _ => { }, _ => false, (_, token) =>
        {
            tokens.Add(token);
            return tokens.Count == 1 ? oldLoad.Task : newLoad.Task;
        });
        var window = new Window { Content = card, Width = 400, Height = 300 };
        using var firstStream = new MemoryStream(TestPng.Rgba(12, 12));
        using var secondStream = new MemoryStream(TestPng.Rgba(16, 16));
        var stale = new Bitmap(firstStream);
        var current = new Bitmap(secondStream);
        try
        {
            window.Show(); Layout(window);
            Assert.Single(tokens);
            window.Content = null; Layout(window);
            Assert.True(tokens[0].IsCancellationRequested);
            window.Content = card; Layout(window);
            Assert.Equal(2, tokens.Count);
            newLoad.SetResult(current);
            for (var i = 0; i < 50 && Find<Image>(card, "DirectoryRowArtwork").Source is null; i++)
            { await Task.Delay(10); Layout(window); }
            Assert.Same(current, Find<Image>(card, "DirectoryRowArtwork").Source);
            oldLoad.SetResult(stale);
            await Task.Delay(20); Layout(window);
            Assert.Same(current, Find<Image>(card, "DirectoryRowArtwork").Source);
        }
        finally { window.Close(); stale.Dispose(); current.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ArtworkLoadingIsLimitedToRealizedRowsNotTheWholeDirectory()
    {
        await using var fixture = new Fixture("Slate", 1040, 680, artwork: true, valleys: 200);
        var window = fixture.Window;
        try
        {
            window.Show(); Layout(window);
            var list = Find<ListBox>(window, "DirectoryResults");
            for (var i = 0; i < 50 && fixture.Handler.ArtRequests.Count == 0; i++)
            { await Task.Delay(10); Layout(window); }
            Assert.InRange(fixture.Handler.ArtRequests.Count, 1, 12);
            list.SelectedIndex = list.ItemCount - 1; list.ScrollIntoView(list.SelectedItem!); Layout(window);
            for (var i = 0; i < 50 && !fixture.Handler.ArtRequests.Any(url => url.Contains("valley")); i++)
            { await Task.Delay(10); Layout(window); }
            Assert.Contains(fixture.Handler.ArtRequests, url => url.Contains("valley"));
            Assert.InRange(fixture.Handler.ArtRequests.Count, 2, 24);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "Slate", 360)]
    [InlineData("de", "Hull", 360)]
    [InlineData("fr", "Paper", 440)]
    [InlineData("es", "Slate", 360)]
    [InlineData("pt-BR", "Hull", 440)]
    public async Task NarrowDockReflowsActionsAndFilterPopupWithoutHorizontalOverflow(string language, string theme, int width)
    {
        await using var fixture = new Fixture(theme, 1040, 680);
        Wandur.Core.Localization.UiLanguage.Apply(language);
        var browser = new WorldBrowserView(fixture.Window.Sessions.Browser(fixture.Catalog), fixture.Catalog);
        var window = new Window { Content = browser, Width = width, Height = 700 };
        try
        {
            window.Show(); Layout(window);
            var row = Find<ListBox>(browser, "DirectoryResults").GetVisualDescendants().OfType<ListBoxItem>().First();
            foreach (var button in row.GetVisualDescendants().OfType<Button>())
            {
                var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), browser)!.Value;
                Assert.InRange(end.X, 0, browser.Bounds.Width);
                Assert.InRange(end.Y, 0, browser.Bounds.Height);
            }
            Assert.Empty(ContrastProbe.Scan(row));
            var filter = Find<Button>(browser, "DirectoryFiltersButton");
            var flyout = Assert.IsType<Flyout>(filter.Flyout);
            flyout.ShowAt(filter); Layout(window);
            var scroll = Assert.IsType<ScrollViewer>(flyout.Content);
            Assert.True(scroll.Bounds.Height > 0);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            var genre = Find<ComboBox>(scroll, "DirectoryThemeFilter");
            genre.SelectedItem = "Fantasy"; Layout(window);
            flyout.Hide();
            // Online now is the site's select in the bar, not a popup checkbox.
            Find<ComboBox>(browser, "DirectoryOnlineChoice").SelectedIndex = 1; Layout(window);
            Assert.True(fixture.Window.Sessions.Browser(fixture.Catalog).Query.OnlineOnly);
            Assert.All(Find<ListBox>(browser, "DirectoryResults").Items.OfType<WorldListing>(), w => Assert.Equal("Fantasy", w.Features.Theme));
            Find<Button>(browser, "DirectoryResetFilters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            Assert.Equal(8, Find<ListBox>(browser, "DirectoryResults").ItemCount);
            Capture(window, $"directory-narrow-{language}-{theme}.png");
        }
        finally { window.Close(); Wandur.Core.Localization.UiLanguage.Apply(""); }
    }

    [AvaloniaTheory]
    [InlineData(1040, 680)]
    [InlineData(1536, 1024)]
    public async Task DirectoryRowsUseTheWholeDockAndSaveWithoutConnecting(int width, int height)
    {
        await using var fixture = new Fixture("Slate", width, height);
        var window = fixture.Window;
        try
        {
            window.Show(); Layout(window);
            var browser = Find<WorldBrowserView>(window);
            var list = Find<ListBox>(browser, "DirectoryResults");
            Assert.True(list.Bounds.Width > browser.Bounds.Width * .9, "Results must use the center dock, not a narrow master/detail column.");
            // The site's rows are taller than the old cards; bring the row into view before using it.
            list.ScrollIntoView(list.Items.OfType<WorldListing>().Single(w => w.Id == "lantern")); Layout(window);
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().First(r => r.DataContext is WorldListing { Id: "lantern" });
            var save = Find<Button>(row, "DirectoryRowSave");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            Assert.Equal("lantern.example.org", Assert.Single(window.Controller.Settings.Profiles).Host);
            Assert.False(window.Controller.HasSession);
            Assert.True(list.IsEffectivelyVisible);
            Assert.False(save.IsEnabled);
            foreach (var button in row.GetVisualDescendants().OfType<Button>())
            {
                var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), row)!.Value;
                Assert.InRange(end.X, 0, row.Bounds.Width);
                Assert.InRange(end.Y, 0, row.Bounds.Height);
            }
            Find<Button>(row, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            Assert.False(list.IsEffectivelyVisible);
            Assert.True(Find<ScrollViewer>(browser, "DirectoryDetailsScroll").IsEffectivelyVisible);
            Find<Button>(browser, "DirectoryBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            Assert.True(list.IsEffectivelyVisible);
            Assert.False(window.Controller.HasSession);
            Capture(window, $"directory-redesign-{width}.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Hull", 1040, 680)]
    [InlineData("Hull", 1536, 1024)]
    [InlineData("Slate", 1040, 680)]
    [InlineData("Slate", 1536, 1024)]
    public async Task EmbeddedDirectoryHasReadableHierarchyAndCompactSelectableCards(string theme, int width, int height)
    {
        await using var fixture = new Fixture(theme, width, height);
        var window = fixture.Window;
        try
        {
            window.Show(); Layout(window);
            var initialList = Find<ListBox>(window, "DirectoryResults");
            initialList.SelectedItem = initialList.Items.OfType<WorldListing>().Single(w => w.Id == "lantern");
            Layout(window);
            var browser = Find<WorldBrowserView>(window);
            var heading = browser.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == L.FindAMUD);
            Assert.True(heading.FontSize >= 20, "The directory needs a page heading above control and result text.");
            var search = Find<TextBox>(browser, "DirectorySearch");
            var list = Find<ListBox>(browser, "DirectoryResults");
            Capture(window, $"directory-polish-{theme.ToLowerInvariant()}-{width}x{height}.png");
            // The site's bar: search takes the free width, the online and sort selects and Filters keep their own.
            var searchField = Find<Border>(browser, "DirectorySearchField");
            foreach (var field in new[] { "DirectoryOnlineField", "DirectorySortField" })
                Assert.True(searchField.Bounds.Width > Find<Border>(browser, field).Bounds.Width, "Search should be the widest field in the bar.");
            Assert.True(searchField.Bounds.Width > 280, "Search should use the available directory width.");
            Assert.InRange(Find<Border>(browser, "DirectoryTitleBar").Bounds.Height, 30, 46);
            Assert.True(list.Bounds.Height > browser.Bounds.Height * .45, $"The directory header must leave room to browse: {list.Bounds.Height} of {browser.Bounds.Height}.");
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.True(rows.Length >= 2, $"Only {rows.Length} rows realized."); 
            foreach (var row in rows)
            {
                var world = Assert.IsType<WorldListing>(row.DataContext);
                var name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == world.Name);
                Assert.True(name.FontSize >= 14, "World names should be readable at the app's normal text size.");
                Assert.InRange(row.Bounds.Height, 125, 260);
                Assert.InRange(row.Bounds.Width, 160, list.Bounds.Width);
                // The site's 5:2 plate: full width on top of a narrow row, beside the text on a wide one.
                var tile = Find<Border>(row, "DirectoryResultIdentity");
                Assert.True(tile.Bounds.Width >= 180 && tile.Bounds.Height >= Math.Min(120, tile.Bounds.Width / 2.5) - 1, $"Plate {tile.Bounds}.");
            }
            // Selection is the accent outline on the same card surface, as the site marks nothing but hover.
            var selected = rows.Single(r => r.IsSelected);
            var card = Find<Border>(selected, "DirectoryResultCard");
            Assert.Equal(Application.Current!.Resources["AccentBrush"], card.BorderBrush);
            var other = rows.First(r => !r.IsSelected);
            Assert.NotEqual(card.BorderBrush, Find<Border>(other, "DirectoryResultCard").BorderBrush);
            Capture(window, $"directory-polish-{theme.ToLowerInvariant()}-{width}x{height}.png");

            // Selection must update the existing detail and command targets, including browser-only worlds.
            list.SelectedItem = list.Items.OfType<WorldListing>().Single(w => w.WebOnly);
            list.ScrollIntoView(list.SelectedItem!);
            Layout(window);
            var webRow = list.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing { WebOnly: true });
            Find<Button>(webRow, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            Assert.Equal("Web Garden", Find<TextBlock>(browser, "DirectoryWorldTitle").Text);
            Assert.False(Find<Button>(browser, "ConnectDirectoryWorld").IsEnabled);
            Assert.False(Find<Button>(browser, "AddDirectoryWorld").IsEnabled);
            Assert.Contains(browser.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, L.PlayInBrowser));
            Find<Button>(browser, "DirectoryBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            search.Text = "Lantern"; Layout(window);
            Assert.Single(list.Items);
            Find<Button>(browser, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            var connect = Find<Button>(browser, "ConnectDirectoryWorld");
            var save = Find<Button>(browser, "AddDirectoryWorld");
            Assert.True(connect.IsEnabled && save.IsEnabled);
            var pageScroll = Find<ScrollViewer>(browser, "DirectoryDetailsScroll");
            foreach (var button in new[] { connect, save })
            {
                var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), pageScroll)!.Value;
                Assert.InRange(end.X, 0, pageScroll.Viewport.Width - WorldBrowserView.ScrollGutter + 1);
                Assert.InRange(end.Y, 0, browser.Bounds.Height);
            }
            var toolbar = Find<Border>(browser, "DirectoryListingToolbar");
            Assert.True(toolbar.IsEffectivelyVisible, "The world page shows its actions.");
            save.Command!.Execute(null);
            Assert.Single(window.Controller.Settings.Profiles);
            Find<Button>(browser, "DirectoryBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            Assert.False(browser.GetVisualDescendants().Contains(toolbar) && toolbar.IsEffectivelyVisible, "Back to the list hides the page's actions.");
            search.Text = "no matching world zzq"; Layout(window);
            Assert.Empty(list.Items);
            Assert.False(browser.GetVisualDescendants().Contains(toolbar) && toolbar.IsEffectivelyVisible);
            Assert.False(pageScroll.IsEffectivelyVisible);
            Find<Button>(browser, "DirectoryResetFilters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.Equal(8, list.Items.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task VisibleRowsLoadThumbnailsAndExploredArtworkStaysUncropped()
    {
        await using var fixture = new Fixture("Hull", 1040, 680, artwork: true);
        try
        {
            fixture.Window.Show(); Layout(fixture.Window);
            var list = Find<ListBox>(fixture.Window, "DirectoryResults");
            var first = list.GetVisualDescendants().OfType<ListBoxItem>().First(r => r.DataContext is WorldListing { Id: "orbital" });
            for (var attempt = 0; attempt < 100 && Find<Image>(first, "DirectoryRowArtwork").Source is null; attempt++)
            { await Task.Delay(10); Layout(fixture.Window); }
            var thumbnail = Assert.IsType<Bitmap>(Find<Image>(first, "DirectoryRowArtwork").Source);
            Assert.InRange(thumbnail.PixelSize.Width, 1, 800);
            Find<Button>(first, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(fixture.Window);
            for (var attempt = 0; attempt < 100 && Find<Image>(fixture.Window, "DirectoryArtwork").Source is null; attempt++)
            { await Task.Delay(10); Layout(fixture.Window); }
            Layout(fixture.Window);
            var image = Find<Image>(fixture.Window, "DirectoryArtwork");
            Assert.NotNull(image.Source);
            // The hero crops to fill, as the site's object-fit: cover does, at a fixed height rather than the picture's own.
            Assert.Equal(Stretch.UniformToFill, image.Stretch); Assert.InRange(image.Bounds.Height, 1, 320);
            Assert.Single(fixture.Handler.ArtRequests, url => url.EndsWith("/orbit.png"));
            Assert.InRange(fixture.Handler.ArtRequests.Count, 1, 2);
            var title = Find<TextBlock>(fixture.Window, "DirectoryWorldTitle");
            Assert.True(title.Bounds.Height > 0);
        }
        finally { fixture.Window.Close(); }
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
        frame.Save(Path.Combine(directory, name), new PngBitmapEncoderOptions());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "wandur-directory-polish-" + Guid.NewGuid());
        private readonly HttpClient _http;
        private readonly WorldCatalog _catalog;
        public WorldCatalog Catalog => _catalog;
        public FixtureHandler Handler { get; }
        public MainWindow Window { get; }

        public Fixture(string theme, int width, int height, bool artwork = false, int valleys = 5)
        {
            Directory.CreateDirectory(_directory);
            var world = new WorldListing
            {
                Id = "lantern", Name = "The Lantern & the Rain", Summary = "A quiet inn. A winding road. Somewhere to begin.",
                Description = "Follow the lanterns through a rain-soaked forest, trade stories at the inn, and discover the old paths beyond the village.\n\nFeatures:\n* Explore the old roads\n* Meet fellow travellers\n* Build a home in the valley",
                Host = "lantern.example.org", Port = 4000, TlsPort = 4001,
                Availability = new() { Online = true }, Population = new() { LatestCount = 42 },
                Features = new() { Theme = "Fantasy", Kind = "MUD", Language = "English" },
                Source = new() { Name = "Test directory" }, Tags = ["Exploration", "Roleplay"],
                BannerUrl = artwork ? "https://art.example.org/lantern.png" : ""
            };
            var worlds = new[] { world, world with { Id = "orbital", Name = "Orbital Station", Host = "orbit.example.org", Summary = "Find a home among the stars.", Description = "Explore the station and its distant outposts.", Features = new() { Theme = "Science fiction", Kind = "MUSH" }, BannerUrl = artwork ? "https://art.example.org/orbit.png" : "" },
                new WorldListing { Id = "web", Name = "Web Garden", WebOnly = true, PlayUrl = "https://garden.example.org", Features = new() { Theme = "Social" } } }
                .Concat(Enumerable.Range(1, valleys).Select(i => world with { Id = "valley" + i, Name = "Valley " + i, Host = $"valley{i}.example.org", Summary = "Explore a distant valley.", Description = "Build a home beside the river.", BannerUrl = artwork && valleys > 5 ? $"https://art.example.org/valley{i}.png" : "" })).ToArray();
            var snapshot = JsonSerializer.Serialize(new { format = "wandur.directory", schema_version = 2, fetched_at = DateTimeOffset.UtcNow, worlds },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var cache = Path.Combine(_directory, "directory.json");
            File.WriteAllText(cache, snapshot);
            Handler = new FixtureHandler(snapshot); _http = new HttpClient(Handler);
            _catalog = new WorldCatalog(cache, new Uri("https://directory.example.org/"), _http);
            var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
            store.Save(new ClientSettings { Theme = theme, UseWorldThemes = false });
            Window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
                new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: _catalog)
                { Width = width, Height = height };
        }

        public async ValueTask DisposeAsync()
        {
            await Window.Sessions.DisposeAsync(); _catalog.Dispose(); _http.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class FixtureHandler(string snapshot) : HttpMessageHandler
    {
        public List<string> ArtRequests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/directory")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(snapshot) });
            if (request.RequestUri.Host == "art.example.org")
            {
                ArtRequests.Add(request.RequestUri.AbsoluteUri);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestPng.Rgba(480, 180)) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
