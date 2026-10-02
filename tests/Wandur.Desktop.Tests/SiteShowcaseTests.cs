using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.Views;

namespace Wandur.Desktop.Tests;

/// <summary>
/// The screenshots on wandur.net's client page: the real client, headless, playing an original fictional world over
/// a loopback connection. The scene is a fantasy road through varied country, so the map shows its terrain colours,
/// with a channel conversation and resource bars, and Find a MUD with the live directory. Set WANDUR_CAPTURE_DIR to
/// keep the renders.
/// </summary>
public sealed class SiteShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("Slate")]
    [InlineData("Paper")]
    public async Task TheLanternRoadRendersAWorkspaceWorthShowing(string theme)
    {
        await using var session = await LanternRoadSession.OpenAsync(new() { Theme = theme });
        var window = session.Window;
        Assert.Equal(LanternRoadSession.Rooms.Length, window.Controller.Map.Snapshot.Rooms.Count);
        session.FitMap();
        Assert.Equal(3, window.GetVisualDescendants().OfType<ResourceBarsView>().Single()
            .GetVisualDescendants().OfType<ProgressBar>().Count());
        Assert.Equal(5, window.GetVisualDescendants().OfType<ChannelMessageList>().Single().Rows.Count);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        Assert.Equal(1600, image.PixelSize.Width);
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            image.Save(Path.Combine(directory, $"showcase-{theme.ToLowerInvariant()}.png"), new PngBitmapEncoderOptions());
        }
    }

    /// <summary>Find a MUD in the main window with the live directory and its real art, filtered to fantasy worlds, then
    /// the first world's page. Read-only against https://api.wandur.net, everything else in a temp folder; skipped unless WANDUR_LIVE=1,
    /// so the test suite never reaches the network.</summary>
    [AvaloniaFact]
    public async Task TheLiveDirectoryRendersAListAndAWorldPageWorthShowing()
    {
        if (Environment.GetEnvironmentVariable("WANDUR_LIVE") != "1") return;
        var directory = Path.Combine(Path.GetTempPath(), "wandur-showcase-live-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var catalog = new Wandur.Core.Discovery.WorldCatalog(Path.Combine(directory, "directory.json"), new Uri("https://api.wandur.net/"), http);
        await catalog.LoadAsync(force: true);
        // Find a MUD as the client shows it: inside the main window, opened from the toolbar, in Slate.
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new ClientSettings { Theme = "Slate", Language = "en", FontSize = 16, UseWorldThemes = false, ClassifyRoomsLocally = false });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(), new MemoryRoomMapStore(),
            new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: catalog) { Width = 1600, Height = 1000 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await window.BrowseWorldsAsync(); Dispatcher.UIThread.RunJobs();
            var browser = window.GetVisualDescendants().OfType<WorldBrowserView>().Single();
            var model = Assert.IsType<Wandur.Desktop.ViewModels.WorldBrowserViewModel>(browser.DataContext);
            model.Query = new() { Facets = new Dictionary<string, string> { ["Theme"] = "Fantasy" } };
            Image[] Plates() => browser.GetVisualDescendants().OfType<Image>().Where(i => i.Name == "DirectoryRowArtwork" && i.IsEffectivelyVisible).ToArray();
            await Settle(window, () => Plates() is { Length: > 0 } plates && plates.All(p => p.Source is not null));
            Save(window, "showcase-directory.png");

            var first = browser.GetVisualDescendants().OfType<Button>().First(b => b.Name == "DirectoryRowExplore" && b.IsEffectivelyVisible);
            first.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Settle(window, () => browser.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "DirectoryArtwork" && i.Source is not null));
            Save(window, "showcase-world.png");
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); Directory.Delete(directory, true); }

        static async Task Settle(Window window, Func<bool> ready)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(30) && !ready())
            {
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            }
            Assert.True(ready(), "the live directory or its art did not arrive in 30 seconds");
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
        }

        static void Save(Window window, string name)
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } captures)
            {
                Directory.CreateDirectory(captures);
                frame.Save(Path.Combine(captures, name), new PngBitmapEncoderOptions());
            }
        }
    }
}
