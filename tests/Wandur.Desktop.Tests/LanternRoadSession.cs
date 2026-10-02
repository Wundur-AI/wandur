using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Mapping;
using Wandur.Core.Scripting;
using Wandur.Core.Settings;
using Wandur.Desktop.Services;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.Views;
using Wandur.Models;

namespace Wandur.Desktop.Tests;

/// <summary>What a Lantern Road session is built with. The defaults are the site's client page showcase; the help
/// screenshots add an opponent mapping, running scripts, an agent and history on top.</summary>
internal sealed class LanternRoadOptions
{
    public string Theme { get; init; } = "Slate";
    /// <summary>The saved worlds after The Lantern Road. Null keeps the showcase's three placeholders.</summary>
    public IReadOnlyList<ConnectionProfile>? OtherWorlds { get; init; }
    public IReadOnlyList<FieldBinding> ExtraBindings { get; init; } = [];
    public IScriptRuntimeFactory Scripts { get; init; } = new RecordingScriptFactory();
    public IWorldScriptLibraryStore ScriptStore { get; init; } = new MemoryScriptLibraryStore();
    public IAgentClientServices? Agents { get; init; }
    public Wandur.Core.History.IHistoryStore? History { get; init; }
    public Func<ClientSettings, ClientSettings>? Settings { get; init; }
    public Wandur.Desktop.Security.IPasswordVault Passwords { get; init; } = new MemoryPasswordVault();
    /// <summary>Called with the loopback port before the window opens, so a script store can be keyed by endpoint.</summary>
    public Action<int>? BeforeOpen { get; init; }
}

/// <summary>
/// The Lantern Road, an original fictional world, played over a loopback connection by the real client: a 21-room map
/// across nine terrains, a guild channel over GMCP, and three resource bars from a protocol mapping. Shared by the
/// site's showcase captures and the help screenshots.
/// </summary>
internal sealed class LanternRoadSession : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly TcpClient _peer;

    private LanternRoadSession(TcpListener listener, TcpClient peer, MainWindow window, ConnectionProfile profile, string directory)
    { _listener = listener; _peer = peer; Window = window; Profile = profile; Directory = directory; }

    public MainWindow Window { get; }
    public ConnectionProfile Profile { get; }
    public string Directory { get; }
    public NetworkStream Stream => _peer.GetStream();
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public WorkspaceController Controller { get; private set; } = null!;

    public static readonly (string Speaker, string Text)[] Guild =
    [
        ("Wren", "Lanterns are lit at the crossroads. Road is clear to the ford."),
        ("Odo", "Watch the marsh after dusk, the will-o-wisps are out again."),
        ("Wren", "Meet at the Wayfarer's Rest? I'll save the corner table."),
        ("Bastian", "Bringing the map. Someone found a cave under the falls."),
        ("Odo", "On my way. Keep the kettle warm.")
    ];

    /// <summary>Explicit coordinates keep the chart the same on every run; the terrains are the client's own palette.</summary>
    public static readonly (string Id, string Name, int X, int Y, string Terrain, string[] Exits)[] Rooms =
    [
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
    ];

    public static async Task<LanternRoadSession> OpenAsync(LanternRoadOptions options)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-lantern-road-" + Guid.NewGuid());
        var listener = new TcpListener(IPAddress.Loopback, 0);
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
                new() { Source = new("GMCP", "Char.Vitals", "/maxmoves"), Target = new("character", "resource", "movement", "maximum"), Label = "Movement" },
                .. options.ExtraBindings
            ]
        };
        var profile = new ConnectionProfile { Name = "The Lantern Road", Host = "127.0.0.1", Port = port, ProtocolMapping = mapping };
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        var settings = new ClientSettings
        {
            Theme = options.Theme, Language = "en", FontSize = 16, UseWorldThemes = false, ClassifyRoomsLocally = false,
            Profiles = [profile, .. options.OtherWorlds ?? [
                new() { Name = "Starfall Reach", Host = "starfall.example" },
                new() { Name = "Emberwake", Host = "emberwake.example" },
                new() { Name = "The Verdant Roads", Host = "verdant.example" }]]
        };
        store.Save(options.Settings?.Invoke(settings) ?? settings);
        options.BeforeOpen?.Invoke(port);
        var window = new MainWindow(new TranscriptDisplayFactory(), store, options.Passwords,
            new MemoryRoomMapStore(), options.Scripts, options.ScriptStore, agents: options.Agents, history: options.History)
        { Width = 1600, Height = 1000 };
        TcpClient? peer = null;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var opening = window.Sessions.OpenAsync(profile);
            peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await opening;
            var session = new LanternRoadSession(listener, peer, window, profile, path) { Controller = window.Controller };
            await session.PlayAsync();
            return session;
        }
        catch
        {
            peer?.Dispose(); listener.Dispose();
            await window.Sessions.DisposeAsync(); window.Close();
            throw;
        }
    }

    /// <summary>Vitals, the guild's conversation, the walked map, then the transcript that leaves the player at the crossroads.</summary>
    private async Task PlayAsync()
    {
        Controller.ClearTranscript();
        await Stream.WriteAsync(new byte[] { 255, 251, 201 });
        await Stream.WriteAsync(Gmcp("Char.Vitals", new { hp = 312, maxhp = 380, mana = 146, maxmana = 220, moves = 188, maxmoves = 240 }));
        foreach (var (speaker, text) in Guild)
            await Stream.WriteAsync(Gmcp("Comm.Channel.Text", new { channel = "guild", talker = speaker, text }));
        // Walked in an order that ends at the crossroads, where the transcript leaves the player.
        foreach (var room in Rooms.Append(Rooms[1]))
            Controller.Map.Observe(new RoomObservation(room.Id, room.Name, "", room.Exits
                .Select(n => n.Split(':')).ToDictionary(n => n[0], n => (string?)n[1]), "The Lantern Road", RoomDataSource.Gmcp)
            { X = room.X, Y = -room.Y, Z = 0, Environment = room.Terrain });
        await WriteAsync(Transcript, "Lantern Crossroads >");
    }

    /// <summary>Sends text as the world, with LF turned into CRLF, and waits until the transcript shows the marker.</summary>
    public async Task WriteAsync(string text, string marker)
    {
        await Stream.WriteAsync(Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n", StringComparison.Ordinal)));
        await SettleAsync(() => Controller.Terminal.PlainText.Contains(marker, StringComparison.Ordinal));
        Assert.Contains(marker, Controller.Terminal.PlainText);
    }

    /// <summary>Sends raw bytes as the world, then lets the client read and draw them until the condition holds.</summary>
    public async Task SendAsync(byte[] bytes, Func<bool>? until = null)
    {
        await Stream.WriteAsync(bytes);
        await SettleAsync(until ?? (() => false), until is null ? 5 : 100);
        if (until is not null) Assert.True(until(), "The world's output did not arrive.");
    }

    public async Task SettleAsync(Func<bool> done, int attempts = 100)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs(); Controller.FlushOutput();
            if (done()) break;
        }
        Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
    }

    /// <summary>Fits the map to the floor, as the showcase shows it.</summary>
    public void FitMap()
    {
        Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
        var mapControl = Window.GetVisualDescendants().OfType<RoomMapControl>().Single();
        mapControl.Model!.FitFloorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
    }

    public static byte[] Gmcp(string package, object payload) =>
        [255, 250, 201, .. Encoding.UTF8.GetBytes(package + " " + JsonSerializer.Serialize(payload)), 255, 240];

    public async ValueTask DisposeAsync()
    {
        try { await Window.Sessions.DisposeAsync(); Window.Close(); }
        finally { _peer.Dispose(); _listener.Dispose(); }
    }

    public static readonly string Transcript = """
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
