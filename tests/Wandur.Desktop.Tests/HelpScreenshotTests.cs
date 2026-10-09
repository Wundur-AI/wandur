using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Agents;
using Wandur.Core.Channels;
using Wandur.Core.History;
using Wandur.Core.Scripting;
using Wandur.Core.Settings;
using Wandur.Core.Storage;
using Wandur.Desktop.Services;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.ViewModels;
using Wandur.Desktop.Views;
using Wandur.Models;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// The pictures on wandur.net's player help pages: the real client, headless, in Slate, playing The Lantern Road
/// over loopback (see <see cref="LanternRoadSession"/>). Every scene asserts what it shows before it is framed, so a
/// refactor that loses a control fails here instead of saving an empty picture. Set WANDUR_CAPTURE_DIR to keep them.
/// </summary>
[Collection(UiLanguageCollection.Name)]
public sealed class HelpScreenshotTests
{
    private const string LanternWatch = """
        // Lantern watch: oil and the road ahead, beside the transcript.
        const watch = mud.panel("lantern-watch", { title: "Lantern watch", dock: "right" });
        const strip = mud.panel("lantern-oil", { dock: "bars" });
        let oil = 8;

        function draw() {
            watch.gauge("oil", { label: "Lantern oil", value: oil, max: 10, warn: 0.3 });
            watch.gauge("wick", { label: "Wick", value: 64, max: 100 });
            watch.label("road", { text: "&YRoad clear to the ford&D" });
            strip.gauge("oil", { label: "Lantern oil", value: oil, max: 10 });
        }

        draw();
        watch.button("refill", { label: "Refill lantern", onClick: () => mud.send("fill lantern") });
        mud.trigger(/^Your lantern gutters/, () => { oil = Math.max(0, oil - 1); draw(); });
        """;

    private const string GuildGreeter = """
        // Wave back when a guildmate arrives at the Rest.
        mud.trigger(/^(\w+) arrives from the east\.$/, match => mud.send("wave " + match[1]));
        """;

    private const string MarshWarning = """
        // Warn before walking into the marsh after dark.
        mud.alias(/^marsh$/, () => mud.echo("Take a lantern: the marsh is bad tonight."));
        """;

    /// <summary>Find a MUD in the main window, a fictional world's page scrolled to its World details card.</summary>
    [AvaloniaFact]
    public async Task FindAMudShowsAWorldsDetails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wandur-help-directory-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        using var catalog = DirectorySiteLookCaptureTests.Fixture.FictionalCatalog(directory, out var http, "Wayfarer Atlas");
        using var _ = http;
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new ClientSettings { Theme = "Slate", Language = "en", FontSize = 16, UseWorldThemes = false, ClassifyRoomsLocally = false });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(), new MemoryRoomMapStore(),
            new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: catalog) { Width = 1600, Height = 1000 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await window.BrowseWorldsAsync(); HelpCapture.Settle(window);
            var browser = Single<WorldBrowserView>(window);
            var list = Single<ListBox>(browser, "DirectoryResults");
            await Until(window, () => list.GetVisualDescendants().OfType<ListBoxItem>().Any());
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().Single(r => r.DataContext is Wandur.Core.Discovery.WorldListing { Id: "starfall" });
            Single<Button>(row, "DirectoryRowExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(window, () => browser.GetVisualDescendants().OfType<Image>().Any(i => i.Name == "DirectoryArtwork" && i.Source is not null));
            var scroll = Single<ScrollViewer>(browser, "DirectoryDetailsScroll");
            var chips = Single<Border>(browser, "DirectoryChipsBar");
            scroll.Offset = new Vector(0, Math.Max(0, chips.TranslatePoint(default, scroll)!.Value.Y + scroll.Offset.Y - 190));
            HelpCapture.Settle(window);
            var details = Single<Border>(browser, "DirectoryWorldDetails");
            var connect = Single<Button>(browser, "ConnectDirectoryWorld");
            var add = Single<Button>(browser, "AddDirectoryWorld");
            var tls = Single<CheckBox>(browser, "DirectoryUseTls");
            Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == L.WorldDetails);
            Assert.Equal(L.Connect2, connect.Content); Assert.Equal(L.AddToMyWorlds, add.Content); Assert.Equal(L.UseTLS, tls.Content);
            foreach (var control in new Control[] { connect, add, tls })
                Assert.True(InView(control, scroll), $"{control.Name} is outside the visible page.");
            Assert.InRange(details.TranslatePoint(default, scroll)!.Value.Y, 0, scroll.Viewport.Height / 2);
            HelpCapture.Window(window, "help-first-world-details.png");
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); Directory.Delete(directory, true); }
    }

    /// <summary>The rest of the help pages, in sequence, from one Lantern Road session (and a second for Starfall Reach).</summary>
    [AvaloniaFact]
    public async Task TheLanternRoadIllustratesThePlayerHelp()
    {
        using var starfall = new TcpListener(IPAddress.Loopback, 0);
        starfall.Start();
        var starfallProfile = new ConnectionProfile { Name = "Starfall Reach", Host = "127.0.0.1", Port = ((IPEndPoint)starfall.LocalEndpoint).Port };
        var scripts = new MemoryScriptLibraryStore();
        var agents = new AgentServices();
        var historyDirectory = Path.Combine(Path.GetTempPath(), "wandur-help-history-" + Guid.NewGuid());
        var database = new ClientDatabase(Path.Combine(historyDirectory, "history.db"));
        var watchId = Guid.NewGuid();
        try
        {
            await using var session = await LanternRoadSession.OpenAsync(new()
            {
                OtherWorlds = [starfallProfile,
                    new() { Name = "Emberwake", Host = "emberwake.example.org", Port = 4000 },
                    new() { Name = "The Verdant Roads", Host = "verdant.example.org", Port = 7777 }],
                ExtraBindings = [
                    Bind("Char.Status", "/name", "character", "identity", "name", "value", "Name", "text"),
                    Bind("Char.Combat", "/enemy", "opponent", "identity", "name", "value", "Name", "text"),
                    Bind("Char.Combat", "/enemyhp", "opponent", "resource", "health", "current", "Health"),
                    Bind("Char.Combat", "/enemymaxhp", "opponent", "resource", "health", "maximum", "Health")],
                // The store a player's own system would name, rather than the test double's label.
                Passwords = new MemoryPasswordVault(OperatingSystem.IsWindows() ? L.WindowsVaultName : OperatingSystem.IsLinux() ? L.LinuxVaultName : L.KeychainName),
                Scripts = new InlineScriptFactory(), ScriptStore = scripts, Agents = agents, History = new SqliteHistoryStore(database),
                Settings = settings => settings with
                {
                    HideHistoryRecordingNotice = true,
                    Profiles = [settings.Profiles[0] with { ChannelRules = new ChannelRuleList([
                        new("trade", @"^\[Trade\] (?<speaker>[A-Za-z]+): (?<text>.*)$", "trade"),
                        new("shout", @"^(?<speaker>[A-Za-z]+) shouts, '(?<text>.*)'$", "shout")]) },
                        .. settings.Profiles.Skip(1)]
                },
                BeforeOpen = port =>
                {
                    var key = $"127.0.0.1:{port}:False";
                    var starter = scripts.Load(key).Single();
                    scripts.Upsert(key, new(watchId, "Lantern watch", LanternWatch));
                    scripts.Upsert(key, new(Guid.NewGuid(), "Guild greeter", GuildGreeter, Enabled: true));
                    scripts.Upsert(key, new(Guid.NewGuid(), "Marsh warning", MarshWarning));
                    scripts.Delete(key, starter.Id);
                }
            });
            var window = session.Window;
            var lantern = window.Sessions.Active;
            await session.SendAsync(LanternRoadSession.Gmcp("Char.Status", new { name = "Odo" }), () => session.Controller.CharacterName == "Odo");
            foreach (var (channel, speaker, text) in new[]
            {
                ("ooc", "Bastian", "Anyone know if the ferry runs at night?"),
                ("chat", "Wren", "New lantern oil at the market, half price till dusk."),
                ("tell", "Wren", "Saved you the corner table. Bring the map?")
            })
                await session.SendAsync(LanternRoadSession.Gmcp("Comm.Channel.Text", new { channel, talker = speaker, text }));
            session.FitMap();

            await MapPanel(session);
            await Channels(session);
            await ResourceBars(session);
            await Diagnostics(session);
            await MapRoute(session);
            await MapEditor(session);
            await WorldEditor(session, agents);
            await SettingsDialog(session);
            await History(session);
            await Scripting(session, watchId);
            await Agent(session);
            await SecondSessionAndPrivateInput(session, starfall, starfallProfile, lantern);
            await ViewMenu(session);
        }
        finally { database.Dispose(); TestFiles.DeleteDirectory(historyDirectory); }
    }

    private static async Task MapPanel(LanternRoadSession session)
    {
        var window = session.Window;
        var map = Single<MapView>(window);
        Assert.Equal("Lantern Crossroads", map.Model.Snapshot.Rooms.Single(r => r.Id == map.Model.Snapshot.CurrentRoomId).Name);
        Assert.Equal(LanternRoadSession.Rooms.Length, map.Model.Snapshot.Rooms.Count);
        Assert.Equal(9, map.Model.Snapshot.Rooms.Select(r => r.Environment).Distinct().Count());
        Assert.NotEmpty(map.Model.Snapshot.Links);
        HelpCapture.Crop(window, "help-map-panel.png", Chrome(map));
        await Task.CompletedTask;
    }

    private static async Task Channels(LanternRoadSession session)
    {
        var window = session.Window;
        var view = Single<ChannelsView>(window);
        var tabs = view.Model.Tabs.Select(t => t.Title).ToArray();
        Assert.Equal(L.ChannelsAll, tabs[0]);
        foreach (var channel in new[] { "guild", "ooc", "chat", "tell" }) Assert.Contains(channel, tabs);
        view.Model.SelectedIndex = 0; HelpCapture.Settle(window);
        var rows = Single<ChannelMessageList>(view).Rows;
        Assert.Equal(8, rows.Count);
        Assert.Contains(rows, row => row.Contains("Bastian", StringComparison.Ordinal) && row.Contains("ferry", StringComparison.Ordinal));
        HelpCapture.Crop(window, "help-channels.png", Chrome(view));

        view.Model.SelectedIndex = view.Model.Tabs.IndexOf(view.Model.Tabs.Single(t => t.Title == "tell")); HelpCapture.Settle(window);
        var reply = Single<TextBox>(view, "ChannelReply");
        Assert.True(view.Model.CanReply);
        reply.Text = "On my way, keep the kettle warm."; HelpCapture.Settle(window);
        var send = Single<Button>(view, "ChannelSend");
        Assert.True(send.IsEffectivelyVisible); Assert.True(send.IsEffectivelyEnabled);
        Assert.True(Single<Border>(view, "ChannelReplyBar").IsEffectivelyVisible);
        Assert.Contains(Single<ChannelMessageList>(view).Rows, row => row.Contains("Saved you the corner table", StringComparison.Ordinal));
        HelpCapture.Crop(window, "help-channels-reply.png", Chrome(view));
        reply.Text = ""; view.Model.SelectedIndex = 0; HelpCapture.Settle(window);
        await Task.CompletedTask;
    }

    private static async Task ResourceBars(LanternRoadSession session)
    {
        var window = session.Window;
        await session.WriteAsync("""

            A will-o-wisp drifts out of the reeds, flickering with cold light.
            \e[1;31mThe marsh wisp attacks you!\e[0m
            You swing your lantern pole and catch the wisp a glancing blow.
            The marsh wisp sears your arm with cold fire.
            \e[32m296/380 hp\e[0m  \e[36m140/220 mana\e[0m  \e[33m181/240 mv\e[0m  \e[31m[wisp: wounded]\e[0m
            \e[90mLantern Crossroads >\e[0m
            """.Replace("\\e", "\u001b", StringComparison.Ordinal), "[wisp: wounded]");
        await session.SendAsync(LanternRoadSession.Gmcp("Char.Vitals", new { hp = 296, maxhp = 380, mana = 140, maxmana = 220, moves = 181, maxmoves = 240 }));
        await session.SendAsync(LanternRoadSession.Gmcp("Char.Combat", new { enemy = "a marsh wisp", enemyhp = 42, enemymaxhp = 100 }),
            () => session.Controller.GameState.Opponent.Resources.GetValueOrDefault("health")?.Percentage == 42);
        var strip = Single<ResourceBarsView>(window);
        var cards = strip.GetVisualDescendants().OfType<ResourceBar>().Select(bar => Assert.IsType<string>(ToolTip.GetTip(bar))).ToArray();
        Assert.Equal(new[] { "Health: 296 / 380", "Mana: 140 / 220", "Movement: 181 / 240", L.VitalsOpponent + " · Health: 42 / 100" }, cards);
        var bounds = HelpCapture.BoundsIn(strip, window);
        HelpCapture.CropRect(window, "help-resource-bars.png", new Rect(bounds.X, bounds.Y - 150, bounds.Width, bounds.Height + 150).Inflate(HelpCapture.Margin));

        // The fight ends: the world clears the opponent and the card goes.
        await session.WriteAsync("""

            The marsh wisp gutters out and is gone.
            \e[90mLantern Crossroads >\e[0m
            """.Replace("\\e", "\u001b", StringComparison.Ordinal), "gutters out and is gone");
        await session.SendAsync(LanternRoadSession.Gmcp("Char.Combat", new { enemy = "", enemyhp = 0, enemymaxhp = 0 }),
            () => strip.GetVisualDescendants().OfType<ResourceBar>().Count() == 3);
    }

    private static async Task Diagnostics(LanternRoadSession session)
    {
        var window = session.Window;
        var controller = session.Controller;
        controller.Pages.SelectedPage = SessionPage.Diagnostics; HelpCapture.Settle(window);
        var diagnostics = Single<ProtocolDiagnosticsView>(window, "ProtocolDiagnostics");
        Assert.True(diagnostics.IsEffectivelyVisible);
        var model = controller.Diagnostics;
        Assert.Contains(model.Entries, e => e.Content?.Name == "Comm.Channel.Text");
        model.Follow = false;
        var messages = Single<ListBox>(diagnostics, "ProtocolMessages");
        messages.SelectedItem = model.Entries.Last(e => e.Content?.Name == "Char.Vitals");
        HelpCapture.Settle(window);
        Assert.Equal("Char.Vitals", model.SelectedEntry?.Content?.Name);
        Assert.True(messages.ContainerFromItem(model.SelectedEntry!) is ListBoxItem { IsSelected: true });
        var detail = diagnostics.GetVisualDescendants().OfType<DiagnosticsBodyEditor>().First(e => e.IsEffectivelyVisible);
        Assert.Contains("\"maxhp\"", detail.Document.Text);
        HelpCapture.Crop(window, "help-resource-bars-diagnostics.png", diagnostics);

        messages.SelectedItem = model.Entries.First(e => e.Content?.Name == "Comm.Channel.Text"); HelpCapture.Settle(window);
        Assert.Contains("\"talker\"", diagnostics.GetVisualDescendants().OfType<DiagnosticsBodyEditor>().First(e => e.IsEffectivelyVisible).Document.Text);
        var status = Single<TextBlock>(Single<MapView>(window), "MapProtocolStatus");
        // The status line names the protocols in use; the full negotiation state is its tooltip (UI review item 15).
        Assert.Equal("GMCP", status.Text);
        Assert.Contains("GMCP: " + L.MapProtocolEnabled, Single<MapView>(window).Model.ProtocolStatus);
        Assert.True(model.Visible.Count >= 10);
        HelpCapture.Window(window, "help-troubleshooting-diagnostics.png");
        messages.SelectedItem = null; model.Follow = true;
        controller.Pages.SelectedPage = SessionPage.Play; HelpCapture.Settle(window);
        await Task.CompletedTask;
    }

    private static async Task MapRoute(LanternRoadSession session)
    {
        var window = session.Window;
        var map = Single<MapView>(window);
        // Wider, as a reader drags the splitter, so the route shows beside the tools.
        var right = (Dock.Model.Core.IDock)window.Workspace.MapTool!.Owner!.Owner!;
        var documents = (Dock.Model.Core.IDock)((Dock.Model.Core.IDock)right.Owner!).VisibleDockables!.OfType<Dock.Model.Controls.IDocumentDock>().Single();
        var (rightShare, documentShare) = (right.Proportion, documents.Proportion);
        right.Proportion = 0.5; documents.Proportion = 0.32;
        HelpCapture.Settle(window);
        map.Model.SelectRoom(map.Model.Snapshot.Rooms.Single(r => r.Name == "Hidden Grotto").Id);
        map.Model.PlanRouteCommand.Execute(null);
        Assert.NotNull(map.Model.PlannedRoute);
        Assert.Equal(L.Format(L.MapRouteReady, map.Model.PlannedRoute!.Steps.Count, map.Model.PlannedRoute.Cost), map.Model.RouteStatus);
        Single<ToggleButton>(window, "MapToolsToggle").IsChecked = true; HelpCapture.Settle(window);
        var tools = Single<Border>(map, "MapToolsPanel");
        var route = Single<Expander>(map, "MapRouteTools");
        route.IsExpanded = true; HelpCapture.Settle(window);
        route.BringIntoView(); HelpCapture.Settle(window);
        var walk = Single<Button>(map, "MapWalkRoute");
        Assert.True(walk.IsEffectivelyVisible); Assert.True(walk.IsEffectivelyEnabled);
        Assert.Contains(map.Model.RouteStatus, route.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        // Fit the floor into the part of the map the tools leave clear.
        map.Model.FitFloorCommand.Execute(null); HelpCapture.Settle(window);
        var canvas = Single<RoomMapControl>(map);
        var covered = tools.Bounds.Width + 12;
        map.Model.ZoomAt(map.Model.Zoom * (canvas.Bounds.Width - covered) / canvas.Bounds.Width, new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2));
        map.Model.Pan(-covered / 2, 0); HelpCapture.Settle(window);
        Assert.True(tools.IsEffectivelyVisible);
        HelpCapture.Crop(window, "help-map-route.png", Chrome(map));
        Single<ToggleButton>(window, "MapToolsToggle").IsChecked = false;
        map.Model.ClearRouteCommand.Execute(null);
        right.Proportion = rightShare; documents.Proportion = documentShare;
        session.FitMap();
        await Task.CompletedTask;
    }

    private static async Task MapEditor(LanternRoadSession session)
    {
        var window = session.Window;
        window.Workspace.OpenMapEditor(session.Controller); HelpCapture.Settle(window);
        var document = Assert.Single(window.Workspace.MapDocuments);
        var editor = Single<MapEditorView>(window);
        document.Model.FitFloorCommand.Execute(null);
        document.Model.IsEditMode = true;
        document.Model.SelectRoom(document.Model.Snapshot.Rooms.Single(r => r.Name == "Below the Falls").Id);
        document.Model.RoomEditor.Description = "Spray hangs over a deep green pool. Behind the curtain of water, a dark gap leads west.";
        document.Model.RoomEditor.Notes = "Cave entrance behind the water, west side.";
        HelpCapture.Settle(window);
        Assert.Equal("Below the Falls", Single<TextBox>(editor, "MapRoomName").Text);
        Assert.Equal("The Lantern Road", Single<TextBox>(editor, "MapRoomArea").Text);
        Assert.True(Single<Control>(editor, "MapEditorInspector").IsEffectivelyVisible);
        HelpCapture.Window(window, "help-map-editor.png");
        document.Model.IsEditMode = false;
        window.Workspace.CloseDockable(document); HelpCapture.Settle(window);
        Assert.Empty(window.Workspace.MapDocuments);
        await Task.CompletedTask;
    }

    private static async Task WorldEditor(LanternRoadSession session, AgentServices agents)
    {
        var window = session.Window;
        var editing = window.EditSessionAutomationAsync(session.Controller, 1);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows.OfType<ProfileDialog>());
        var model = Assert.IsType<ProfileEditorViewModel>(dialog.DataContext);
        dialog.Width = 1100; dialog.Height = 760; HelpCapture.Settle(dialog);
        try
        {
            // Login: what a player fills in for a world that asks for a name and password.
            Assert.True(model.IsLogin);
            model.Username = "lantern-demo"; model.RememberPassword = true; model.Password = "lantern-demo-pass"; model.AutoLogin = true;
            HelpCapture.Settle(dialog);
            var password = Single<TextBox>(dialog, "LoginPassword");
            Assert.Equal('●', password.PasswordChar); Assert.True(password.IsEffectivelyEnabled);
            Assert.Equal("lantern-demo", Single<TextBox>(dialog, "LoginUsername").Text);
            Assert.True(Single<CheckBox>(dialog, "RememberPassword").IsChecked); Assert.True(Single<CheckBox>(dialog, "AutoLogin").IsChecked);
            HelpCapture.Window(dialog, "help-logging-in-login.png");

            // Connection, as a reader types the world's published address.
            model.SectionIndex = 0; model.Host = "lanternroad.example.org"; model.Port = 4000; HelpCapture.Settle(dialog);
            var connection = Single<ScrollViewer>(dialog, "ConnectionSection");
            Assert.True(connection.IsEffectivelyVisible);
            Assert.Equal("lanternroad.example.org", Single<TextBox>(dialog, "WorldHost").Text);
            Assert.Equal(4000, Single<NumericUpDown>(dialog, "WorldPort").Value);
            Assert.Contains(connection.GetVisualDescendants().OfType<CheckBox>(), c => c.Content as string == L.UseTLSServerMustSupportIt);
            Assert.Contains(connection.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == L.TextEncoding);
            HelpCapture.Window(dialog, "help-troubleshooting-connection.png");
            model.Host = "127.0.0.1"; model.Port = session.Port;

            // Channels: the two rules this world was taught.
            model.SectionIndex = 5; HelpCapture.Settle(dialog);
            var rules = Single<ChannelRulesView>(dialog);
            Assert.True(rules.IsEffectivelyVisible);
            Assert.Equal(new[] { "trade", "shout" }, rules.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "ChannelRuleChannel").Select(t => t.Text).ToArray());
            Assert.All(rules.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name == "ChannelRuleEnabled"), c => Assert.True(c.IsChecked));
            Assert.Equal(2, rules.GetVisualDescendants().OfType<TextBox>().Count(t => t.Name == "ChannelRulePattern" && t.Text!.Contains("(?<speaker>", StringComparison.Ordinal)));
            HelpCapture.Window(dialog, "help-channels-rules.png");

            // Agent settings, saved for this world.
            model.SectionIndex = 4; HelpCapture.Settle(dialog);
            var agent = Single<AgentSettingsView>(dialog);
            Single<TabControl>(agent, "AgentEditors").SelectedIndex = 2; HelpCapture.Settle(dialog);
            // The editor shows the server's address; the provider adds its API path, so the saved endpoint is .../v1.
            Assert.Equal("http://localhost:1234", Single<TextBox>(agent, "AgentServerAddress").Text);
            Assert.Equal("http://localhost:1234/v1", model.Agent!.Endpoint);
            Assert.Equal(agents.Profile.Model, Single<TextBox>(agent, "AgentModel").Text);
            Assert.Equal(0, Single<ComboBox>(agent, "AgentProvider").SelectedIndex);
            Assert.Contains("north | north", agent.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AgentCommands" && t.IsEffectivelyVisible).Text);
            HelpCapture.Window(dialog, "help-agent-settings.png");

            // Scripts: a short script in the code editor, completion open after "mud.".
            model.SectionIndex = 2; HelpCapture.Settle(dialog);
            var library = Single<ScriptLibraryView>(dialog);
            var scripts = Assert.IsType<ScriptLibraryViewModel>(library.DataContext);
            scripts.Selected = scripts.Items.Single(i => i.Name == "Lantern watch"); HelpCapture.Settle(dialog);
            var code = Single<ScriptCodeEditor>(dialog, "WorldScriptSource");
            Assert.Equal(LanternWatch, code.Text);
            code.TextArea.Focus();
            var trigger = code.Text.IndexOf("mud.trigger", StringComparison.Ordinal);
            code.CaretOffset = trigger + "mud.".Length;
            code.ShowCompletion(); HelpCapture.Settle(dialog);
            Assert.NotNull(code.Completion);
            Assert.Contains(code.Completion!.CompletionList.CompletionData, d => d.Text == "trigger");
            Assert.Contains(code.Completion.CompletionList.CompletionData, d => d.Text == "panel");
            HelpCapture.Window(dialog, "help-scripting-editor.png", code.Completion);
            code.Completion.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(LanternWatch, code.Text);
        }
        finally
        {
            model.ConfirmDiscardAsync = () => Task.FromResult(true);
            dialog.Close(); Dispatcher.UIThread.RunJobs();
            await editing;
        }
        Assert.Equal("127.0.0.1", session.Controller.Settings.Profiles[0].Host);
        Assert.Null(session.Controller.Settings.Profiles[0].PasswordId);
    }

    private static async Task SettingsDialog(LanternRoadSession session)
    {
        var window = session.Window;
        var opening = window.PreferencesAsync();
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows.OfType<OptionsDialog>());
        var model = Assert.IsType<PreferencesViewModel>(dialog.DataContext);
        dialog.Width = 1100; dialog.Height = 760; HelpCapture.Settle(dialog);
        try
        {
            Assert.True(model.IsGeneral);
            var history = Single<CheckBox>(dialog, "HistoryEnabled");
            Assert.True(history.IsChecked); Assert.True(history.IsEffectivelyVisible);
            Assert.True(Single<ComboBox>(dialog, "HistoryRetention").IsEffectivelyVisible);
            HelpCapture.Window(dialog, "help-history-settings.png");

            model.SectionIndex = 1;
            model.DuplicateThemeCommand.Execute(null);
            model.ThemeName = "Lantern Dusk";
            var accent = model.Colors.First(c => c.Label == L.PaletteAccent);
            accent.Hex = "#E0A84E";
            HelpCapture.Settle(dialog);
            Assert.True(model.IsCustom);
            var scroll = Single<ScrollViewer>(dialog, "AppearanceScroll");
            Assert.True(scroll.IsEffectivelyVisible);
            var theme = Single<ComboBox>(dialog, "ThemeChoice");
            scroll.Offset = new Vector(0, Math.Max(0, theme.TranslatePoint(default, scroll)!.Value.Y + scroll.Offset.Y - 40));
            HelpCapture.Settle(dialog);
            var pickers = scroll.GetVisualDescendants().OfType<ColorPicker>().Where(p => InView(p, scroll)).ToArray();
            Assert.True(pickers.Length >= 4, $"{pickers.Length} color pickers in view.");
            Assert.Contains(scroll.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "#E0A84E" && InView(t, scroll));
            Assert.Equal("Lantern Dusk", model.ThemeName);
            HelpCapture.Window(dialog, "help-themes-appearance.png");
        }
        finally
        {
            dialog.Close(); Dispatcher.UIThread.RunJobs();
            await opening;
        }
        Assert.Equal("Slate", session.Controller.Settings.Theme);
        Assert.Empty(session.Controller.Settings.CustomThemes);
    }

    private static async Task History(LanternRoadSession session)
    {
        var window = session.Window;
        await window.ShowHistoryAsync(); Dispatcher.UIThread.RunJobs();
        var history = Assert.Single(window.OwnedWindows.OfType<HistoryWindow>());
        history.Width = 1100; history.Height = 760;
        try
        {
            await Until(history, () => !history.Model.IsBusy);
            Single<TextBox>(history, "HistoryQuery").Text = "\"leaning signpost\"";
            Single<TextBox>(history, "HistoryWorld").Text = "Lantern Road";
            Single<TextBox>(history, "HistoryCharacter").Text = "Odo";
            Single<CalendarDatePicker>(history, "HistoryFrom").SelectedDate = DateTime.Today.AddDays(-7);
            Single<CalendarDatePicker>(history, "HistoryUntil").SelectedDate = DateTime.Today.AddDays(1);
            Dispatcher.UIThread.RunJobs();
            await history.Model.RefreshAsync();
            await Until(history, () => !history.Model.IsBusy);
            var hit = Assert.Single(history.Model.Results);
            Assert.Contains("leaning signpost", hit.Entry.Text, StringComparison.Ordinal);
            Assert.Equal("Odo", hit.Session.CharacterName);
            var results = Single<ListBox>(history, "HistoryResults");
            results.SelectedIndex = 0;
            await Until(history, () => !history.Model.IsContextBusy && history.Model.Transcript.Count > 0);
            var transcript = Single<TextBox>(history, "HistoryTranscript");
            Assert.Contains("leaning signpost", transcript.SelectedText, StringComparison.Ordinal);
            Assert.Contains("Four roads meet", transcript.Text, StringComparison.Ordinal);
            HelpCapture.Window(history, "help-history.png");
        }
        finally { history.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    private static async Task Scripting(LanternRoadSession session, Guid watchId)
    {
        var window = session.Window;
        var automation = session.Controller.Pages.Automation;
        Assert.Equal(new[] { "Lantern watch", "Guild greeter", "Marsh warning" }, automation.Scripts.Select(s => s.Name).ToArray());
        var watch = automation.Scripts.Single(s => s.Entry.Id == watchId);
        watch.Enabled = true;
        await session.SettleAsync(() => watch.Entry.Runtime.IsRunning && session.Controller.ScriptLibrary.Panels.Panels.Count == 2);
        Assert.True(watch.Entry.Runtime.IsRunning, watch.Entry.Runtime.Error);
        await session.WriteAsync("\nYour lantern gutters in the wind.\n\u001b[90mLantern Crossroads >\u001b[0m\n", "Your lantern gutters");
        var strip = Single<ResourceBarsView>(window);
        await session.SettleAsync(() => strip.ScriptCards.Count == 1 && Math.Abs(strip.ScriptCards[0].GetVisualDescendants().OfType<ProgressBar>().Single().Value - 70) < 0.01);
        Assert.Equal(70, strip.ScriptCards.Single().GetVisualDescendants().OfType<ProgressBar>().Single().Value, 3);

        var button = Single<SessionScriptsButton>(window);
        button.Flyout!.ShowAt(button); HelpCapture.Settle(window);
        var content = (Control)((Flyout)button.Flyout).Content!;
        var switches = content.GetVisualDescendants().OfType<CheckBox>().ToArray();
        Assert.Equal(new bool?[] { true, true, false }, switches.Select(s => s.IsChecked).ToArray());
        Assert.True(content.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReloadSessionAutomation").IsEffectivelyVisible);
        HelpCapture.Crop(window, "help-scripting-menu.png", Single<Border>(window, "OutputFooter"), content);
        button.Flyout.Hide(); HelpCapture.Settle(window);

        var rail = Single<ScriptPanelRailView>(window);
        Assert.True(rail.IsEffectivelyVisible);
        var panel = Single<ScriptPanelView>(window);
        Assert.Equal(2, panel.GetVisualDescendants().OfType<ResourceBar>().Count());
        var refill = Assert.Single(panel.GetVisualDescendants().OfType<Button>());
        Assert.True(refill.IsEffectivelyVisible);
        Assert.Contains(refill.GetVisualDescendants().OfType<TextBlock>(), t => string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []) + t.Text is { } text && text.Contains("Refill lantern", StringComparison.Ordinal));
        Assert.Equal(4, strip.GetVisualDescendants().OfType<ResourceBar>().Count());
        session.FitMap();
        HelpCapture.Window(window, "help-scripting-panel.png");
    }

    private static async Task Agent(LanternRoadSession session)
    {
        var window = session.Window;
        var agent = session.Controller.Agent!;
        Assert.Equal(2, agent.Goals.Count);
        Assert.True(agent.Goals[0].Enabled);
        var step = agent.StepCommand.ExecuteAsync(null);
        await ReadCommandAsync(session, "look");
        await session.WriteAsync("""

            \e[1;33mLantern Crossroads\e[0m
            Four roads meet beneath a leaning signpost. Lamps hang from every
            arm of it, swaying in the wind.
            A lamplighter nods to you as he passes.

            \e[37mExits:\e[0m \e[32mnorth\e[0m, \e[32meast\e[0m, \e[32msouth\e[0m, \e[32mwest\e[0m
            \e[90mLantern Crossroads >\e[0m
            """.Replace("\\e", "\u001b", StringComparison.Ordinal), "A lamplighter nods to you as he passes.");
        await step.WaitAsync(TimeSpan.FromSeconds(10));
        await session.SettleAsync(() => !agent.IsBusy);
        Assert.Contains("look", agent.Activity, StringComparison.Ordinal);

        var button = Single<Button>(window, "SessionAgent");
        button.Flyout!.ShowAt(button); HelpCapture.Settle(window);
        var host = Assert.IsType<ScrollViewer>(((Flyout)button.Flyout).Content);
        var view = Single<AgentSessionView>(host);
        foreach (var expander in view.GetVisualDescendants().OfType<Expander>().Take(1)) expander.IsExpanded = true;
        HelpCapture.Settle(window);
        var goals = view.GetVisualDescendants().OfType<RadioButton>().ToArray();
        Assert.Equal(2, goals.Length); Assert.True(goals[0].IsChecked);
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Content as string == L.AgentPlay && b.IsEffectivelyVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("look", StringComparison.Ordinal) == true);
        Assert.True(Single<Button>(window, "PlaySessionAgent").IsEffectivelyVisible);
        HelpCapture.Crop(window, "help-agent-menu.png", Single<Border>(window, "OutputFooter"), host);
        button.Flyout.Hide(); HelpCapture.Settle(window);
    }

    private static async Task SecondSessionAndPrivateInput(LanternRoadSession session, TcpListener listener, ConnectionProfile profile, SessionTab lantern)
    {
        var window = session.Window;
        var opening = window.Sessions.OpenAsync(profile);
        using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await opening;
        var starfall = window.Sessions.Active;
        Assert.NotSame(lantern, starfall);
        var stream = peer.GetStream();
        async Task World(byte[] bytes, Func<bool> until, string what)
        {
            await stream.WriteAsync(bytes);
            for (var i = 0; i < 100 && !until(); i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); starfall.Controller.FlushOutput(); }
            Assert.True(until(), what + " did not arrive: " + starfall.Controller.Terminal.PlainText);
        }
        starfall.Controller.ClearTranscript();
        await World(Encoding.UTF8.GetBytes("""
            \e[36m   S T A R F A L L   R E A C H\e[0m
            The beacon at the edge of the settled systems.

            \e[33mStation news\e[0m
              * The survey skiff Kestrel is back at the eastern berth.
              * Docking fees at Beacon Anchorage are waived this week.
              * The observation deck reopens after the storm repairs.
              * New pilots: the training sims are open every cycle.

            \e[33mTonight on the concourse\e[0m
              Trader's market from second watch.
              Story circle at the observation deck, all welcome.

            \e[33mFrom the harbourmaster\e[0m
              The storm front over the outer ring has passed. Shuttles to the
              lower decks run on the half hour again, and the cargo lifts on
              the western spine are back in service. Pilots flying the long
              route to the relay should log their flight plans at the tower.
              Salvage claims from the storm are heard at the claims office
              every third watch. Please keep the concourse clear of crates.

            \e[33mCrew wanted\e[0m
              Kestrel: a navigator for the survey run past the relay.
              Lumen's Rest: two cooks, one with a steady hand for spice.
              The Brightwater: deckhands for the ice haul, three cycles out.

            \e[33mRecent arrivals\e[0m
              Mira, Tarin, Sable, and the crew of the Kestrel.

            Players online: 38. Ships in dock: 12.

            By what name are you known? lantern-demo
            Welcome back, lantern-demo.
            Password:
            """.Replace("\\e", "\u001b", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal).TrimEnd('\n', '\r', ' ') + " "),
            () => starfall.Controller.Terminal.PlainText.Contains("Password:", StringComparison.Ordinal), "The password prompt");
        await World([255, 251, 1], () => starfall.Controller.IsPrivate, "Private input");
        HelpCapture.Settle(window);
        var terminal = window.GetVisualDescendants().OfType<TerminalView>().Single(t => t.IsEffectivelyVisible);
        var input = Single<TextBox>(terminal, "CommandInput");
        input.Text = "xxxxxxxxxxxx"; HelpCapture.Settle(window);
        Assert.Equal('●', input.PasswordChar);
        var hint = Single<TextBlock>(terminal, "CommandHint");
        Assert.Equal(L.PrivateHiddenFromEchoAndHistory, hint.Text); Assert.True(hint.IsEffectivelyVisible);
        var composer = HelpCapture.BoundsIn(Single<Border>(terminal, "Composer"), window);
        var footer = HelpCapture.BoundsIn(Single<Border>(terminal, "OutputFooter"), window);
        HelpCapture.CropRect(window, "help-logging-in-private.png",
            new Rect(composer.X, composer.Y - 260, composer.Width, footer.Bottom - composer.Y + 260).Inflate(HelpCapture.Margin));
        input.Text = "";

        // Both sessions open, from the Workspace panel.
        window.Sessions.Select(lantern); HelpCapture.Settle(window);
        var navigation = Single<WorkspaceNavigationView>(window);
        var model = Assert.IsType<WorkspaceNavigationViewModel>(navigation.DataContext);
        Assert.Equal(2, model.OpenEntries.Count(e => e.Key is SessionTab));
        // Saved worlds are a panel of their own, under the Workspace.
        var saved = Single<ListBox>(window, "WorldProfiles");
        Assert.Equal(new[] { "Emberwake", "Starfall Reach", "The Lantern Road", "The Verdant Roads" },
            saved.Items.OfType<ConnectionProfile>().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(4, saved.GetVisualDescendants().OfType<ListBoxItem>().Count(i => i.IsEffectivelyVisible));
        HelpCapture.CropRect(window, "help-first-world-workspace.png",
            HelpCapture.BoundsIn(Chrome(navigation), window).Union(HelpCapture.BoundsIn(Chrome(saved), window)).Inflate(HelpCapture.Margin));
        await window.Sessions.CloseAsync(starfall); HelpCapture.Settle(window);
    }

    private static async Task ViewMenu(LanternRoadSession session)
    {
        var window = session.Window;
        // The window menu is the one Windows and Linux show; on macOS the same items are in the system menu bar.
        var menu = Single<Menu>(window, "MainMenu");
        menu.IsVisible = true; HelpCapture.Settle(window);
        var view = menu.Items.OfType<MenuItem>().Single(m => m.Header as string == L.View);
        view.IsSubMenuOpen = true; HelpCapture.Settle(window);
        var items = view.Items.OfType<MenuItem>().ToArray();
        foreach (var key in new[] { L.Workspace, L.SavedWorlds, L.MapPanel, L.ChannelsPanel })
        {
            var item = items.Single(i => i.Header as string == key);
            Assert.True(item.IsChecked, key); Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType); Assert.True(item.IsEffectivelyVisible);
        }
        var restore = items.Single(i => i.Header as string == L.RestorePanels);
        var popup = HelpCapture.BoundsIn(restore.GetVisualAncestors().OfType<Border>().Last(), window);
        HelpCapture.CropRect(window, "help-themes-view-menu.png", new Rect(0, 0, popup.Right + 120, popup.Bottom + 80), restore);
        view.IsSubMenuOpen = false; menu.IsVisible = !OperatingSystem.IsMacOS(); HelpCapture.Settle(window);
        await Task.CompletedTask;
    }

    /// <summary>Reads what the client sent until the command arrives, telnet negotiation and all.</summary>
    private static async Task ReadCommandAsync(LanternRoadSession session, string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var seen = new List<byte>();
        var buffer = new byte[512];
        while (!Encoding.Latin1.GetString(seen.ToArray()).Contains(command + "\r\n", StringComparison.Ordinal))
        {
            var read = await session.Stream.ReadAsync(buffer, timeout.Token);
            Assert.True(read > 0, "The world's connection closed.");
            seen.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    private static FieldBinding Bind(string package, string path, string entity, string category, string key, string member, string label, string conversion = "number")
        => new() { Source = new("GMCP", package, path), Target = new(entity, category, key, member), Label = label, Conversion = conversion };

    /// <summary>The dock panel around a tool view: its header and its body.</summary>
    private static Control Chrome(Control view) => view.GetVisualAncestors().OfType<Dock.Avalonia.Controls.ToolChromeControl>().First();

    private static bool InView(Control control, ScrollViewer scroll)
    {
        var top = control.TranslatePoint(default, scroll)!.Value.Y;
        return control.IsEffectivelyVisible && top >= 0 && top + control.Bounds.Height <= scroll.Viewport.Height;
    }

    private static T Single<T>(Visual root, string? name = null) where T : Control
    {
        var matches = root.GetVisualDescendants().OfType<T>().Where(c => name is null || c.Name == name).ToArray();
        Assert.True(matches.Length == 1, $"Expected one {typeof(T).Name} {name}, found {matches.Length}.");
        return matches[0];
    }

    private static async Task Until(TopLevel top, Func<bool> ready)
    {
        for (var i = 0; i < 300 && !ready(); i++) { await Task.Delay(10); HelpCapture.Settle(top, 1); }
        Assert.True(ready(), "The view did not settle.");
    }

    /// <summary>A saved agent for The Lantern Road and a model that decides to look around.</summary>
    private sealed class AgentServices : IAgentClientServices, IAgentProfileStore, IAgentProviderResolver, IAgentModelProvider
    {
        public AgentProfile Profile = new()
        {
            Endpoint = "http://localhost:1234/v1", Model = "llama-3.1-8b-instruct", ResponseTimeoutSeconds = 10,
            Goals = [new(Guid.NewGuid(), "Walk the Lantern Road east to Willow Ford, looking at each room on the way.") { Name = "Scout the road" },
                new(Guid.NewGuid(), "Wait at the Wayfarer's Rest and note who comes and goes.", false) { Name = "Keep watch at the Rest" }],
            Commands = "look | look | Look at the room\nnorth | north | Walk north\nsouth | south | Walk south\neast | east | Walk east\nwest | west | Walk west\nscore | score | Check your health"
        };
        public string Key => "openai-compatible";
        public IAgentProfileStore Profiles => this;
        public IAgentProviderResolver Providers => this;
        public event Action<Guid>? Saved;
        public AgentProfile Load(string worldKey) => Profile;
        public void Save(string worldKey, AgentProfile profile) { Profile = profile; Saved?.Invoke(profile.Id); }
        public IAgentModelProvider Resolve(string key) => this;
        public Task<string?> ReadCredentialAsync(AgentProfile profile) => Task.FromResult<string?>(null);
        public Task<AgentProfile> SaveAsync(string worldKey, AgentProfile profile, string key, bool forget) { Save(worldKey, profile); return Task.FromResult(profile); }
        public Task<AgentDecision> DecideAsync(AgentRequest request, string? key, CancellationToken token)
            => Task.FromResult(new AgentDecision("look", "Check the crossroads before choosing a road.", "At Lantern Crossroads; roads lead north, east, south and west."));
        public Task<IReadOnlyList<string>> ListModelsAsync(AgentProfile profile, string? key, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
