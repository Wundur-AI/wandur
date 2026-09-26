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

/// <summary>Adult worlds stay out of the directory unless asked for, and a player count is live only while fresh.</summary>
[Collection(UiLanguageCollection.Name)]
public sealed class DirectoryAdultAndFreshnessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-26T12:00:00Z");

    [AvaloniaFact]
    public async Task AdultWorldsAreHiddenByDefaultShownWithTheToggleAndNeverHiddenFromSavedWorlds()
    {
        var adult = new WorldListing { Id = "velvet", Name = "Velvet Hours", Host = "velvet.example.org", Port = 4000, AdultContent = true,
            Features = new() { Theme = "Social" }, Tags = ["Romance", "Social"], Availability = new() { Online = true } };
        var plain = new WorldListing { Id = "harbor", Name = "Quiet Harbor", Host = "harbor.example.org", Port = 4000, AdultContent = false,
            Features = new() { Theme = "Fantasy" } };
        var unflagged = new WorldListing { Id = "moor", Name = "Grey Moor", Host = "moor.example.org", Port = 4000, Features = new() { Theme = "Fantasy" } };
        // The user saved another adult world: that one is theirs and stays visible in the directory too.
        var mine = adult with { Id = "lantern-house", Name = "Lantern House", Host = "lantern-house.example.org", Tags = ["Nightlife"] };
        var saved = mine.ToProfile();
        await using var fixture = new Fixture([adult, plain, unflagged, mine], new ClientSettings { Profiles = [saved] });
        var browser = new WorldBrowserView(fixture.Model, fixture.Catalog);
        var library = new WorldLibraryView(fixture.Sessions, () => { });
        var window = new Window { Content = new StackPanel { Children = { browser, library } }, Width = 1100, Height = 1400 };
        try
        {
            window.Show(); Layout(window);
            var list = Find<ListBox>(browser, "DirectoryResults");
            Assert.DoesNotContain(list.Items.OfType<WorldListing>(), w => w.Id == "velvet");
            Assert.Contains(list.Items.OfType<WorldListing>(), w => w.Id == "lantern-house");
            Assert.Equal(3, list.ItemCount);
            Assert.Equal(L.Format(L.WorldsToExplore, 3), Find<TextBlock>(browser, "DirectoryCount").Text);
            // The saved adult world is marked in its row.
            list.ScrollIntoView(list.Items.OfType<WorldListing>().Single(w => w.Id == "lantern-house")); Layout(window);
            var mineRow = list.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing { Id: "lantern-house" });
            Assert.Equal(L.AdultChip, ((TextBlock)Find<Border>(mineRow, "DirectoryRowAdult").Child!).Text);
            // A search that names the other one, and the tag facet, still do not reveal it.
            Find<TextBox>(browser, "DirectorySearch").Text = "velvet"; Layout(window);
            Assert.Equal(0, list.ItemCount);
            Assert.DoesNotContain("Romance", fixture.Model.FacetOptions["Tag"]);
            Find<TextBox>(browser, "DirectorySearch").Text = ""; Layout(window);
            // The saved worlds list is the user's own: an adult world they saved stays there.
            Assert.Contains(Find<ListBox>(library, "WorldProfiles").Items.OfType<ConnectionProfile>(), p => p.Host == "lantern-house.example.org");

            var filter = Find<Button>(browser, "DirectoryFiltersButton");
            var flyout = Assert.IsType<Flyout>(filter.Flyout);
            flyout.ShowAt(filter); Layout(window);
            var toggle = Find<CheckBox>(Assert.IsType<ScrollViewer>(flyout.Content), "DirectoryAdultFilter");
            Assert.Equal(L.ShowAdultWorlds, toggle.Content);
            toggle.IsChecked = true; Layout(window);
            flyout.Hide();
            Assert.True(fixture.Model.Query.ShowAdult);
            Assert.Equal(4, list.ItemCount);
            Assert.Equal(L.Format(L.WorldsToExplore, 4), Find<TextBlock>(browser, "DirectoryCount").Text);
            Assert.Contains("Romance", fixture.Model.FacetOptions["Tag"]);

            // The world view marks it.
            list.ScrollIntoView(list.Items.OfType<WorldListing>().Single(w => w.Id == "velvet")); Layout(window);
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing { Id: "velvet" });
            Find<Button>(row, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            Assert.Equal(L.AdultChip, ((TextBlock)Find<Border>(browser, "DirectoryAdultChip").Child!).Text);
            Find<Button>(browser, "DirectoryBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);

            // The setting lives with the other directory filters: it survives leaving and returning to the page.
            window.Content = null; Layout(window);
            var again = new WorldBrowserView(fixture.Model, fixture.Catalog);
            window.Content = again; Layout(window);
            var againFlyout = Assert.IsType<Flyout>(Find<Button>(again, "DirectoryFiltersButton").Flyout);
            Assert.True(Find<CheckBox>(Assert.IsType<ScrollViewer>(againFlyout.Content), "DirectoryAdultFilter", visualOnly: false).IsChecked);
            Assert.Equal(4, Find<ListBox>(again, "DirectoryResults").ItemCount);
            // Clearing the filters hides adult worlds again.
            fixture.Model.ResetFiltersCommand.Execute(null); Layout(window);
            Assert.Equal(3, Find<ListBox>(again, "DirectoryResults").ItemCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(30, true)]
    [InlineData(180, false)]
    public async Task AWandurCountIsLiveOnlyWhileFreshAndOtherwiseReadsAsHistory(int minutesOld, bool live)
    {
        var world = new WorldListing { Id = "starfall", Name = "Starfall", Host = "starfall.example.org", Port = 4000,
            Availability = new() { Online = true }, Features = new() { Theme = "Science fiction" },
            Population = new() { LatestCount = 142, Source = "wandur", ObservedAt = Now.AddMinutes(-minutesOld) } };
        var copied = world with { Id = "copied", Name = "Copied", Host = "copied.example.org",
            Population = new() { LatestCount = 86, Source = "mudverse", ObservedAt = Now.AddMinutes(-10) } };
        await using var fixture = new Fixture([world, copied], new ClientSettings());
        var browser = new WorldBrowserView(fixture.Model, fixture.Catalog);
        var window = new Window { Content = browser, Width = 1280, Height = 1500 };
        try
        {
            window.Show(); Layout(window);
            var list = Find<ListBox>(browser, "DirectoryResults");
            string LiveText(string id) => Find<StackPanel>(list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(r => r.DataContext is WorldListing w && w.Id == id), "DirectoryLive").Children.OfType<TextBlock>().Single().Text!;
            Assert.Equal(live ? L.Format(L.OnlineCount, 142) : L.StatusOnline, LiveText("starfall"));
            // Another listing's count is never live, however recent.
            Assert.Equal(L.StatusOnline, LiveText("copied"));

            var row = list.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is WorldListing { Id: "starfall" });
            Find<Button>(row, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
            var text = browser.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            if (live)
            {
                Assert.Contains(L.Format(L.PlayersOnlineCount, 142), text);
                Assert.Contains(L.Format(L.PlayersObservedAgo, 142, L.Format(L.MinutesAgo, 30)), text);
            }
            else
            {
                Assert.DoesNotContain(L.Format(L.PlayersOnlineCount, 142), text);
                Assert.Contains(L.Format(L.CountedAgo, "Wandur", 142, L.Format(L.HoursAgo, 3)), text);
            }
        }
        finally { window.Close(); }
    }

    private static T Find<T>(Visual root, string name, bool visualOnly = true) where T : Control
    {
        var matches = root.GetVisualDescendants().OfType<T>().Where(c => c.Name == name).ToArray();
        if (!visualOnly && matches.Length == 0)
            matches = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants((Avalonia.LogicalTree.ILogical)root).OfType<T>().Where(c => c.Name == name).ToArray();
        Assert.True(matches.Length == 1, $"Expected one {typeof(T).Name} named {name}, found {matches.Length}.");
        return matches[0];
    }

    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "wandur-directory-adult-" + Guid.NewGuid());
        public WorldCatalog Catalog { get; }
        public SessionWorkspace Sessions { get; }
        public WorldBrowserViewModel Model { get; }

        public Fixture(WorldListing[] worlds, ClientSettings settings)
        {
            Directory.CreateDirectory(_directory);
            var cache = Path.Combine(_directory, "directory.json");
            File.WriteAllText(cache, JsonSerializer.Serialize(new { format = "wandur.directory", schema_version = 2, fetched_at = Now, worlds },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            Catalog = new WorldCatalog(cache, new Uri("http://offline.invalid/"), OfflineHttp.Client(), new FixedClock(Now));
            var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
            store.Save(settings with { UseWorldThemes = false });
            Sessions = new SessionWorkspace(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
                new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: Catalog);
            Model = new WorldBrowserViewModel(Catalog, Sessions);
        }

        public async ValueTask DisposeAsync()
        {
            Model.Dispose(); await Sessions.DisposeAsync(); Catalog.Dispose();
            Directory.Delete(_directory, true);
        }
    }
}
