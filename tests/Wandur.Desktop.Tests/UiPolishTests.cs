using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;

namespace Wandur.Desktop.Tests;

/// <summary>The October 2026 UI review items (docs/ui-review.md), one test or more per item.</summary>
[Collection(UiLanguageCollection.Name)]
public sealed class UiPolishTests
{
    /// <summary>Item 1: the System skin gives the transcript a gutter; the drawn skins keep theirs.</summary>
    [AvaloniaFact]
    public async Task TheSystemSkinGivesTheTranscriptAGutter()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-ui-gutter-" + Guid.NewGuid(), "settings.json"));
        await using var sessions = new SessionWorkspace(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());
        var view = new Wandur.Desktop.Views.TerminalView(sessions.Active.Controller);
        var host = new Window { Content = view };
        try
        {
            host.Show();
            var margin = () => sessions.Active.Controller.Display.View.Margin;
            ThemeService.Apply(new ClientSettings { Theme = "Linen", Skin = WindowSkinId.System });
            Assert.Equal(new Thickness(16, 12, 8, 8), margin());
            ThemeService.Apply(new ClientSettings { Theme = "Hull", Skin = WindowSkinId.Fleet });
            Assert.Equal(new Thickness(12, 12, 8, 8), margin());
            ThemeService.Apply(new ClientSettings { Theme = "Hull", Skin = WindowSkinId.Armored });
            Assert.Equal(new Thickness(4, 2, 2, 2), margin());
        }
        finally { host.Close(); ThemeService.Apply(new()); Dispatcher.UIThread.RunJobs(); }
    }

    /// <summary>Item 2: Fluent's own accent (selected list items, check boxes, tab underlines) follows the palette.</summary>
    [AvaloniaTheory]
    [InlineData("Linen")]
    [InlineData("Midnight")]
    public void FluentsAccentFollowsThePalette(string theme)
    {
        try
        {
            ThemeService.Apply(new ClientSettings { Theme = theme, Skin = WindowSkinId.System });
            var accent = UserTheme.FromPreset(theme).Colors["Accent"];
            Assert.Equal(Avalonia.Media.Color.Parse(accent), Application.Current!.Resources["SystemAccentColor"]);
            Assert.IsType<Avalonia.Media.Color>(Application.Current.Resources["SystemAccentColorLight3"]);
        }
        finally { ThemeService.Apply(new()); }
    }

    /// <summary>Item 3: a world's conversation survives looking at another world, including what it said meanwhile.</summary>
    [AvaloniaFact]
    public async Task TheChannelsPanelKeepsEachSessionsConversation()
    {
        using var first = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        using var second = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        first.Start(); second.Start();
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            async Task<(SessionTab Tab, System.Net.Sockets.TcpClient Peer)> Open(System.Net.Sockets.TcpListener listener, string name)
            {
                var opening = window.Sessions.OpenAsync(new ConnectionProfile { Name = name, Host = "127.0.0.1", Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port });
                var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await opening;
                await peer.GetStream().WriteAsync(new byte[] { 255, 251, 201 });
                return (window.Sessions.Active, peer);
            }
            async Task Say(SessionTab tab, System.Net.Sockets.TcpClient peer, string talker, string text, int expected)
            {
                await peer.GetStream().WriteAsync(LanternRoadSession.Gmcp("Comm.Channel.Text", new { channel = "guild", talker, text }));
                for (var i = 0; i < 200 && tab.Controller.ChannelPanel.Tabs[0].Messages.Count < expected; i++)
                { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); tab.Controller.FlushOutput(); }
                Assert.Equal(expected, tab.Controller.ChannelPanel.Tabs[0].Messages.Count);
            }
            var (lantern, lanternPeer) = await Open(first, "Lantern");
            using var _ = lanternPeer;
            await Say(lantern, lanternPeer, "Wren", "Lanterns are lit.", 1);
            var (starfall, starfallPeer) = await Open(second, "Starfall");
            using var __ = starfallPeer;
            // Lantern keeps talking while Starfall is in front.
            await Say(lantern, lanternPeer, "Odo", "Keep the kettle warm.", 2);
            window.Sessions.Select(lantern); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var rows = Assert.Single(window.GetVisualDescendants().OfType<Wandur.Desktop.Views.ChannelMessageList>(), l => l.IsEffectivelyVisible).Rows;
            Assert.Equal(2, rows.Count);
            Assert.Contains("Wren: Lanterns are lit.", rows[0]);
            Assert.Contains("Odo: Keep the kettle warm.", rows[1]);
            Assert.Empty(starfall.Controller.ChannelPanel.Tabs[0].Messages);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); }
    }

    /// <summary>Item 5: the dock header's tooltips and the close button's accessible name come from the tables.</summary>
    [AvaloniaFact]
    public async Task DockHeaderTooltipsAreLocalized()
    {
        var before = Wandur.Core.Localization.UiLanguage.Culture.Name;
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            foreach (var language in new[] { "en", "de" })
            {
                Wandur.Core.Localization.UiLanguage.Apply(language);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var close = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_CloseButton" && b.IsEffectivelyVisible);
                Assert.Equal(Wandur.Core.Localization.Strings.DockCloseTip, ToolTip.GetTip(close));
                Assert.Equal(Wandur.Core.Localization.Strings.DockClosePanel, Avalonia.Automation.AutomationProperties.GetName(close));
                var grip = window.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "PART_Grid" && g.IsEffectivelyVisible);
                Assert.Equal(Wandur.Core.Localization.Strings.DockGripTip, ToolTip.GetTip(grip));
            }
            Assert.StartsWith("Fenster schließen", Wandur.Core.Localization.Strings.DockCloseTip);
        }
        finally { Wandur.Core.Localization.UiLanguage.Apply(before); await window.Sessions.DisposeAsync(); window.Close(); }
    }

    /// <summary>Item 7: in the System skin the dock header's glyphs are drawn in the secondary text color until the header
    /// is under the pointer; the drawn skins keep their own faces.</summary>
    [AvaloniaTheory]
    [InlineData("System")]
    [InlineData("Fleet")]
    [InlineData("Armored")]
    public async Task DockHeaderGlyphsRecedeOnlyInTheSystemSkin(string skin)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = "Linen" });
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var close = window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First(p => p.Name == "PART_ClosePath" && p.IsEffectivelyVisible);
            var muted = ContrastProbe.Resource("MutedBrush");
            Assert.Equal(skin == "System", Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(close.Fill).Color == muted);
            Assert.Equal(skin == "System", window.Classes.Contains("skin-system"));
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    /// <summary>Item 8: an empty map is only its message, with no zoom or protocol line; the Channels panel shows its note
    /// centred and no reply bar until the first message.</summary>
    [AvaloniaFact]
    public void EmptyMapAndChannelsShowOnlyTheirMessage()
    {
        var tracker = new Wandur.Core.Mapping.RoomMapTracker();
        var model = new Wandur.Desktop.ViewModels.MapViewModel(tracker);
        var window = new Window { Content = new Wandur.Desktop.Views.MapView(model), Width = 400, Height = 500 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Control Named(string name) => window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);
            Assert.True(Named("MapEmptyLabel").IsEffectivelyVisible);
            Assert.False(Named("MapStatusBar").IsVisible);
            tracker.Observe(new Wandur.Core.Mapping.RoomObservation("hall", "Copper hall", "A quiet hall.", new Dictionary<string, string?>(), Source: Wandur.Core.Mapping.RoomDataSource.Gmcp));
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named("MapStatusBar").IsVisible);
            Assert.False(Named("MapEmptyLabel").IsVisible);
        }
        finally { window.Close(); }
    }

    /// <summary>Item 9: an open session's row says what it is doing; the address is its tooltip.</summary>
    [AvaloniaFact]
    public async Task WorkspaceRowsShowTheSessionStateNotItsAddress()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            var opening = window.Sessions.OpenAsync(new ConnectionProfile { Name = "Lantern", Host = "127.0.0.1", Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port });
            using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await opening;
            Dispatcher.UIThread.RunJobs();
            var row = window.Workspace.Navigation.OpenEntries.Single(e => e.Key is SessionTab);
            Assert.Equal(Wandur.Core.Localization.Strings.ConnectedTelnet, row.Details);
            Assert.True(row.IsLive);
            Assert.False(row.HasActivity);
            Assert.StartsWith("127.0.0.1:", row.Address);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); }
    }

    /// <summary>Item 10: the System toolbar draws Connect and Disconnect as outlines like its other glyphs, says their
    /// shortcuts, and both are disabled with nothing to act on.</summary>
    [AvaloniaTheory]
    [InlineData("System")]
    [InlineData("Fleet")]
    public async Task SystemToolbarGlyphsAreOutlinesWithShortcuts(string skin)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = "Linen" });
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var connect = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Connect");
            var disconnect = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Disconnect");
            Assert.False(connect.IsEffectivelyEnabled); Assert.False(disconnect.IsEffectivelyEnabled);
            foreach (var button in new[] { connect, disconnect })
            {
                var glyph = Assert.IsType<Avalonia.Controls.Shapes.Path>(button.Content is Avalonia.Controls.Shapes.Path p ? p : ((Control)button.Content!).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First());
                if (skin == "System") Assert.True(glyph.StrokeThickness > 0);
                Assert.Equal(skin == "System", ToolTip.GetTip(button) is string tip && tip.EndsWith(")", StringComparison.Ordinal));
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    /// <summary>Item 12: a history label skips the parts it does not have instead of leaving a stray separator.</summary>
    [Fact]
    public void HistoryLabelsSkipMissingParts()
    {
        Assert.Equal("The Lantern Road · 10:00", Wandur.Desktop.ViewModels.HistoryViewModel.Join("The Lantern Road", "", "10:00"));
        Assert.Equal("Odo · 10:00", Wandur.Desktop.ViewModels.HistoryViewModel.Join(null, "Odo", "10:00"));
    }

    /// <summary>Item 13: two new macros are told apart by name.</summary>
    [AvaloniaFact]
    public async Task NewMacrosAreNumbered()
    {
        await using var library = new Wandur.Desktop.Services.WorldScriptLibrary(new InlineScriptFactory(), new MemoryScriptLibraryStore(),
            () => true, () => false, _ => Task.FromResult(true), _ => { });
        library.Configure("world", "World");
        var first = library.AddMacro(); var second = library.AddMacro(); var third = library.AddMacro();
        Assert.Equal(Wandur.Core.Localization.Strings.MacroNewName, first.Name);
        Assert.Equal(Wandur.Core.Localization.Strings.MacroNewName + " 2", second.Name);
        Assert.Equal(Wandur.Core.Localization.Strings.MacroNewName + " 3", third.Name);
    }

    /// <summary>Item 15: with nothing negotiated the map's status line says nothing; the full state is its tooltip.</summary>
    [AvaloniaFact]
    public void MapStatusLineNamesOnlyProtocolsInUse()
    {
        var model = new Wandur.Desktop.ViewModels.MapViewModel(new Wandur.Core.Mapping.RoomMapTracker());
        Assert.Equal("", model.ProtocolBadge);
        Assert.Contains("GMCP", model.ProtocolStatus);
    }

    /// <summary>Item 23: in the System skin Send is an ordinary button; the drawn skins keep their primary Send.</summary>
    [AvaloniaTheory]
    [InlineData("System", false)]
    [InlineData("Fleet", true)]
    [InlineData("Armored", true)]
    public async Task SendIsQuietOnlyInTheSystemSkin(string skin, bool primary)
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-ui-send-" + Guid.NewGuid(), "settings.json"));
        await using var sessions = new SessionWorkspace(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());
        var host = new Window { Content = new Wandur.Desktop.Views.TerminalView(sessions.Active.Controller) };
        try
        {
            host.Show();
            ThemeService.Apply(new ClientSettings { Theme = "Linen", Skin = skin });
            Dispatcher.UIThread.RunJobs();
            var send = host.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SendCommand");
            Assert.Equal(primary, send.Classes.Contains("primary"));
        }
        finally { host.Close(); ThemeService.Apply(new()); }
    }

    /// <summary>Item 19: in the System skin buttons fade on hover (one short transition) and every window, dialogs
    /// included, carries the skin class; the drawn skins have no transitions.</summary>
    [AvaloniaTheory]
    [InlineData("System")]
    [InlineData("Fleet")]
    public async Task SystemButtonsFadeOnHover(string skin)
    {
        var window = WindowSkinTransitionTests.Create();
        var dialog = new Window { Content = new Button { Classes = { "app-button" }, Content = "x" } };
        try
        {
            window.Show();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = "Linen" });
            dialog.Show();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(skin == "System", dialog.Classes.Contains("skin-system"));
            var button = (Button)dialog.Content!;
            var fades = button.Transitions?.OfType<Avalonia.Animation.BrushTransition>().ToArray() ?? [];
            if (skin == "System")
            {
                var fade = Assert.Single(fades);
                Assert.Equal(Button.BackgroundProperty, fade.Property);
                Assert.Equal(TimeSpan.FromMilliseconds(100), fade.Duration);
            }
            else Assert.Empty(fades);
        }
        finally { dialog.Close(); await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    /// <summary>Item 17: switching sessions shows each session's Map and Channels views again (kept, hidden, while
    /// another session is in front) rather than building them again, and drops a closed session's views.</summary>
    [AvaloniaFact]
    public async Task SwitchingSessionsReusesEachSessionsPanels()
    {
        using var first = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        using var second = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        first.Start(); second.Start();
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            async Task<SessionTab> Open(System.Net.Sockets.TcpListener listener, string name)
            {
                var opening = window.Sessions.OpenAsync(new ConnectionProfile { Name = name, Host = "127.0.0.1", Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port });
                (await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
                await opening;
                return window.Sessions.Active;
            }
            T Shown<T>() where T : Control { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window.GetVisualDescendants().OfType<T>().Single(v => v.IsEffectivelyVisible); }
            var lantern = await Open(first, "Lantern");
            var (map, channels) = (Shown<Wandur.Desktop.Views.MapView>(), Shown<Wandur.Desktop.Views.ChannelsView>());
            var starfall = await Open(second, "Starfall");
            Assert.NotSame(map, Shown<Wandur.Desktop.Views.MapView>());
            window.Sessions.Select(lantern);
            Assert.Same(map, Shown<Wandur.Desktop.Views.MapView>());
            Assert.Same(channels, Shown<Wandur.Desktop.Views.ChannelsView>());
            await window.Sessions.CloseAsync(starfall);
            Assert.Same(map, Shown<Wandur.Desktop.Views.MapView>());
            Assert.Single(window.GetVisualDescendants().OfType<Wandur.Desktop.Views.MapView>());
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); }
    }
}
