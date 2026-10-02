using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Mapping;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.Views;
using Wandur.Models;

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
    [InlineData("Hull")]
    [InlineData("Paper")]
    public async Task TheLanternRoadRendersAWorkspaceWorthShowing(string theme)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-showcase-" + Guid.NewGuid());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var mapping = new WorldMapping
        {
            WorldId = "lantern-road-demo", Endpoint = new("127.0.0.1", port),
            SchemaFingerprint = new('b', 64), GeneratedAt = DateTimeOffset.UtcNow,
            Bindings = [
                new() { Source = new("GMCP", "Char.Vitals", "/hp"), Target = new("character", "resource", "health", "current"), Label = "Health" },
                new() { Source = new("GMCP", "Char.Vitals", "/maxhp"), Target = new("character", "resource", "health", "maximum"), Label = "Health" },
                new() { Source = new("GMCP", "Char.Vitals", "/mana"), Target = new("character", "resource", "mana", "current"), Label = "Mana" },
                new() { Source = new("GMCP", "Char.Vitals", "/maxmana"), Target = new("character", "resource", "mana", "maximum"), Label = "Mana" },
                new() { Source = new("GMCP", "Char.Vitals", "/moves"), Target = new("character", "resource", "movement", "current"), Label = "Movement" },
                new() { Source = new("GMCP", "Char.Vitals", "/maxmoves"), Target = new("character", "resource", "movement", "maximum"), Label = "Movement" }
            ]
        };
        var profile = new ConnectionProfile { Name = "The Lantern Road", Host = "127.0.0.1", Port = port, ProtocolMapping = mapping };
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings
        {
            Theme = theme, Language = "en", FontSize = 16, UseWorldThemes = false, ClassifyRoomsLocally = false,
            Profiles = [profile,
                new() { Name = "Starfall Reach", Host = "starfall.example" },
                new() { Name = "Emberwake", Host = "emberwake.example" },
                new() { Name = "The Verdant Roads", Host = "verdant.example" }]
        });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore())
        { Width = 1600, Height = 1000 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var opening = window.Sessions.OpenAsync(profile);
            using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await opening;
            window.Controller.ClearTranscript();
            var stream = peer.GetStream();
            await stream.WriteAsync(new byte[] { 255, 251, 201 });
            await stream.WriteAsync(Gmcp("Char.Vitals", new { hp = 312, maxhp = 380, mana = 146, maxmana = 220, moves = 188, maxmoves = 240 }));
            foreach (var (speaker, text) in new[]
            {
                ("Wren", "Lanterns are lit at the crossroads. Road is clear to the ford."),
                ("Odo", "Watch the marsh after dusk, the will-o-wisps are out again."),
                ("Wren", "Meet at the Wayfarer's Rest? I'll save the corner table."),
                ("Bastian", "Bringing the map. Someone found a cave under the falls."),
                ("Odo", "On my way. Keep the kettle warm.")
            })
                await stream.WriteAsync(Gmcp("Comm.Channel.Text", new { channel = "guild", talker = speaker, text }));

            // Explicit coordinates keep the chart the same on every run; the terrains are the client's own palette.
            var rooms = new (string Id, string Name, int X, int Y, string Terrain, string[] Exits)[]
            {
                ("rest", "The Wayfarer's Rest", 0, 0, "indoor", ["east:cross"]),
                ("cross", "Lantern Crossroads", 1, 0, "road", ["west:rest", "east:road2", "north:gate", "south:meadow"]),
                ("gate", "Old Town Gate", 1, -1, "city", ["south:cross", "north:market", "east:lane"]),
                ("market", "Market Square", 1, -2, "city", ["south:gate", "east:well"]),
                ("well", "Well Street", 2, -2, "city", ["west:market", "south:lane"]),
                ("lane", "Chandler's Lane", 2, -1, "city", ["west:gate", "north:well"]),
                ("road2", "The Lantern Road", 2, 0, "road", ["west:cross", "east:road3"]),
                ("road3", "The Lantern Road", 3, 0, "road", ["west:road2", "east:ford", "north:wood1"]),
                ("wood1", "Edge of Hollowwood", 3, -1, "forest", ["south:road3", "north:wood2", "east:wood3"]),
                ("wood2", "Hollowwood Deeps", 3, -2, "forest", ["south:wood1"]),
                ("wood3", "Mossy Clearing", 4, -1, "forest", ["west:wood1", "north:hill"]),
                ("hill", "Watcher's Hill", 4, -2, "grassland", ["south:wood3", "east:peak"]),
                ("peak", "Greywind Pass", 5, -2, "mountain", ["west:hill"]),
                ("ford", "Willow Ford", 4, 0, "water", ["west:road3", "east:road4"]),
                ("road4", "The Lantern Road", 5, 0, "road", ["west:ford", "south:falls"]),
                ("falls", "Below the Falls", 5, 1, "water", ["north:road4", "west:cave"]),
                ("cave", "Hidden Grotto", 4, 1, "cave", ["east:falls"]),
                ("meadow", "Barley Meadow", 1, 1, "grassland", ["north:cross", "east:marsh", "south:farm"]),
                ("farm", "Hollis Farmstead", 1, 2, "indoor", ["north:meadow"]),
                ("marsh", "Lantern Marsh", 2, 1, "swamp", ["west:meadow", "east:marsh2"]),
                ("marsh2", "Reedbank", 3, 1, "swamp", ["west:marsh"])
            };
            // Walked in an order that ends at the crossroads, where the transcript leaves the player.
            foreach (var room in rooms.Append(rooms[1]))
                window.Controller.Map.Observe(new RoomObservation(room.Id, room.Name, "", room.Exits
                    .Select(n => n.Split(':')).ToDictionary(n => n[0], n => (string?)n[1]), "The Lantern Road", RoomDataSource.Gmcp)
                { X = room.X, Y = -room.Y, Z = 0, Environment = room.Terrain });

            await stream.WriteAsync(Encoding.UTF8.GetBytes(Transcript.Replace("\n", "\r\n", StringComparison.Ordinal)));
            for (var attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs(); window.Controller.FlushOutput();
                if (window.Controller.Terminal.PlainText.Contains("Lantern Crossroads >", StringComparison.Ordinal)) break;
            }
            Assert.Contains("Lantern Crossroads >", window.Controller.Terminal.PlainText);
            Assert.Equal(rooms.Length, window.Controller.Map.Snapshot.Rooms.Count);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var mapControl = window.GetVisualDescendants().OfType<RoomMapControl>().Single();
            mapControl.Model!.FitFloorCommand.Execute(null);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
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
        finally { await window.Sessions.DisposeAsync(); window.Close(); }
    }

    /// <summary>Find a MUD with the live directory and its real art, filtered to fantasy worlds, then the first world's
    /// page. Read-only against https://api.wandur.net, everything else in a temp folder; skipped unless WANDUR_LIVE=1,
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
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new ClientSettings { Theme = "Ember", Language = "en", UseWorldThemes = false });
        var sessions = new SessionWorkspace(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: catalog);
        var model = sessions.Browser(catalog);
        model.Query = new() { Facets = new Dictionary<string, string> { ["Theme"] = "Fantasy" } };
        var browser = new WorldBrowserView(model, catalog);
        var window = new Window { Content = browser, Width = 1280, Height = 900 };
        try
        {
            window.Show();
            Image[] Plates() => browser.GetVisualDescendants().OfType<Image>().Where(i => i.Name == "DirectoryRowArtwork" && i.IsEffectivelyVisible).ToArray();
            await Settle(window, () => Plates() is { Length: > 0 } plates && plates.All(p => p.Source is not null));
            Save(window, "showcase-directory.png");

            var first = browser.GetVisualDescendants().OfType<Button>().First(b => b.Name == "DirectoryRowExplore" && b.IsEffectivelyVisible);
            first.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.Height = 1000;
            await Settle(window, () => browser.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "DirectoryArtwork" && i.Source is not null));
            Save(window, "showcase-world.png");
        }
        finally { window.Close(); await sessions.DisposeAsync(); Directory.Delete(directory, true); }

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

    private static byte[] Gmcp(string package, object payload) =>
        [255, 250, 201, .. Encoding.UTF8.GetBytes(package + " " + JsonSerializer.Serialize(payload)), 255, 240];

    private static readonly string Transcript = """
        \e[33mT H E   L A N T E R N   R O A D\e[0m
        Every road remembers who walked it.

        \e[90m> look\e[0m

        \e[1;33mThe Wayfarer's Rest\e[0m
        \e[90mLantern Crossroads / The Common Room\e[0m

        A fire crackles under a mantel hung with old lanterns, each one
        carried here by a traveller who never came back for it. Rain
        ticks against the shutters. Someone has left a map pinned to
        the bar, a red thread looping east toward the falls.

        \e[36mWren\e[0m is here, polishing a brass spyglass.

        \e[33mWren says, "If you're heading east, take a lantern. The marsh is bad tonight."\e[0m

        \e[37mExits:\e[0m \e[32meast\e[0m

        \e[90m> east\e[0m

        \e[1;33mLantern Crossroads\e[0m
        Four roads meet beneath a leaning signpost. Lamps hang from every
        arm of it, swaying in the wind. North, the old town's gate; south,
        barley fields run down toward the marsh.

        \e[37mExits:\e[0m \e[32mnorth\e[0m, \e[32meast\e[0m, \e[32msouth\e[0m, \e[32mwest\e[0m

        \e[90m> guild On my way. Keep the kettle warm.\e[0m
        \e[35m[Guild] You:\e[0m On my way. Keep the kettle warm.

        \e[32m312/380 hp\e[0m  \e[36m146/220 mana\e[0m  \e[33m188/240 mv\e[0m
        \e[90mLantern Crossroads >\e[0m

        """.Replace("\\e", "\u001b", StringComparison.Ordinal);
}
