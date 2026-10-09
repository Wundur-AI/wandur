using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;
using Wandur.Desktop.ViewModels;
using Wandur.Desktop.Views;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// Saved worlds is a dock panel of its own, under the Workspace on the left: Add and Find in its title bar, a double
/// click or Enter to connect, Delete to remove (after asking), and everything else in the row's menu.
/// </summary>
public sealed class SavedWorldsPanelTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "wandur-saved-panel-" + Guid.NewGuid());
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private WorldCatalog? _catalog;
    private MainWindow? _window;

    private ConnectionProfile Listed => new() { Name = "Legends of the Jedi", Host = "127.0.0.1", Port = Port };
    private ConnectionProfile Unlisted => new() { Name = "Emberwake", Host = "127.0.0.1", Port = Port + 1 };
    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private (MainWindow Window, WorldLibraryView View, WorldLibraryViewModel Model, ToolChromeControl Chrome) Open(string skin = "System", string theme = "Linen", bool worlds = true)
    {
        Directory.CreateDirectory(_path);
        _listener.Start();
        var listing = new WorldListing { Id = "legends-of-the-jedi", Name = "Legends of the Jedi", Host = "127.0.0.1", Port = Port, Summary = "A galaxy shaped by its players." };
        File.WriteAllText(Path.Combine(_path, "directory.json"), JsonSerializer.Serialize(
            new { schema_version = 2, format = "wandur.directory", fetched_at = DateTimeOffset.UtcNow, worlds = new[] { listing } },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        _catalog = new WorldCatalog(Path.Combine(_path, "directory.json"));
        var store = new SettingsStore(Path.Combine(_path, "settings.json"));
        if (worlds) store.Save(new ClientSettings { Profiles = [Listed with { Id = ListedId }, Unlisted with { Id = UnlistedId }] });
        var window = _window = new MainWindow(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: _catalog) { Width = 1440, Height = 900 };
        window.Show(); PanelHeaderActionTests.Settle(window);
        window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = theme });
        PanelHeaderActionTests.Settle(window);
        var chrome = new DockDropPreviewTests.DockDrag(window).Panel("saved-worlds");
        var view = chrome.GetVisualDescendants().OfType<WorldLibraryView>().Single();
        return (window, view, (WorldLibraryViewModel)view.DataContext!, chrome);
    }

    private static readonly Guid ListedId = Guid.NewGuid();
    private static readonly Guid UnlistedId = Guid.NewGuid();

    [AvaloniaTheory]
    [MemberData(nameof(DockDropPreviewTests.Looks), MemberType = typeof(DockDropPreviewTests))]
    public void SavedWorldsIsAPanelUnderTheWorkspaceWithAddAndFindInItsTitleBar(string skin, string theme)
    {
        var (window, view, _, chrome) = Open(skin, theme);
        var saved = window.Workspace.SavedWorldsTool!;
        Assert.Equal(L.SavedWorlds, saved.Title);
        var dock = Assert.IsAssignableFrom<IDock>(saved.Owner);
        Assert.Equal("saved-worlds-dock", dock.Id);
        var column = Assert.IsAssignableFrom<IDock>(dock.Owner);
        Assert.Equal(new[] { "left", "saved-worlds-dock" }, column.VisibleDockables!.Where(d => d is IDock { Id: not null } and not IProportionalDockSplitter).Select(d => d.Id));
        Assert.Same(column, window.Workspace.WorldsTool!.Owner!.Owner);
        var workspace = new DockDropPreviewTests.DockDrag(window).Panel("worlds");
        Assert.True(workspace.TranslatePoint(default, window)!.Value.Y < chrome.TranslatePoint(default, window)!.Value.Y);
        // The Workspace keeps Find a MUD and the open sessions; the saved list is only here.
        Assert.DoesNotContain(workspace.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WorldProfiles");
        Assert.Single(window.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "WorldProfiles");

        var header = PanelHeader.HostOf(chrome)!;
        Assert.False(header.IsInRow);
        Assert.Equal(new[] { "AddSavedWorld", "FindSavedWorld" }, header.Buttons.Select(b => b.Name));
        Assert.Equal(L.AddAWorld, ToolTip.GetTip(header.Buttons[0]));
        Assert.Equal(L.SavedWorldsFind, ToolTip.GetTip(header.Buttons[1]));
        // No row of buttons in the panel any more.
        foreach (var name in new[] { "ConnectSavedWorld", "EditSavedWorld", "DeleteSavedWorld", "BrowseWorlds" })
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == name);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Border>(), b => b.Name == "WorldLibraryToolbar");
        DockDropPreviewTests.Capture(window, $"saved-worlds-{skin}-{theme}");
    }

    [AvaloniaFact]
    public void AddOpensTheWorldEditor()
    {
        var (window, _, _, chrome) = Open();
        PanelHeaderActionTests.Named(chrome, "AddSavedWorld").Command!.Execute(null);
        PanelHeaderActionTests.Settle(window);
        var dialog = Assert.Single(window.OwnedWindows.OfType<ProfileDialog>());
        dialog.Close();
    }

    [AvaloniaFact]
    public void FindFiltersTheListAndEscapeClosesIt()
    {
        var (window, view, model, chrome) = Open();
        var find = Assert.IsType<ToggleButton>(PanelHeaderActionTests.Named(chrome, "FindSavedWorld"));
        var box = view.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "SavedWorldsFilter");
        Assert.False(box.IsVisible);
        find.IsChecked = true; PanelHeaderActionTests.Settle(window);
        Assert.True(model.IsFilterVisible && box.IsEffectivelyVisible && box.IsFocused);
        window.KeyTextInput("ember"); PanelHeaderActionTests.Settle(window);
        Assert.Equal("Emberwake", Assert.Single(model.Profiles).Name);
        box.Text = "nothing like it"; PanelHeaderActionTests.Settle(window);
        Assert.Empty(model.Profiles);
        Assert.True(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SavedWorldsNoMatch").IsEffectivelyVisible);
        Assert.False(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SavedWorldsEmpty").IsVisible);
        box.Text = "127.0.0.1:" + Port; PanelHeaderActionTests.Settle(window);
        Assert.Equal("Legends of the Jedi", Assert.Single(model.Profiles).Name);
        box.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        PanelHeaderActionTests.Settle(window);
        Assert.False(model.IsFilterVisible);
        Assert.False(find.IsChecked);
        Assert.Equal("", model.Filter);
        Assert.Equal(2, model.Profiles.Count);
    }

    [AvaloniaFact]
    public void TheEmptyPanelKeepsItsHint()
    {
        var (_, view, model, _) = Open(worlds: false);
        Assert.True(model.IsEmpty);
        var hint = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SavedWorldsEmpty");
        Assert.True(hint.IsEffectivelyVisible);
        Assert.Equal(L.KeepYourFavoriteWorldsHereAddOneToStart, hint.Text);
    }

    [AvaloniaFact]
    public async Task TheRowMenuConnectsEditsDuplicatesExploresAndDeletes()
    {
        var (window, view, model, _) = Open();
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "WorldProfiles");
        var listedRow = Row(list, "Legends of the Jedi");
        Assert.Equal(L.Format(L.DoubleClickToConnect, "Legends of the Jedi", $"127.0.0.1:{Port}"), ToolTip.GetTip(listedRow.GetVisualDescendants().OfType<Grid>().First()));
        Assert.EndsWith("right-click for more", L.DoubleClickToConnect);
        var menu = OpenMenu(window, listedRow);
        Assert.Equal(new[] { "ConnectWorldMenu", "ConnectNewTabWorldMenu", "EditWorldMenu", "DuplicateWorldMenu", "ExploreWorldMenu", "DeleteWorldMenu" },
            menu.Items.OfType<MenuItem>().Select(m => m.Name));
        Assert.Equal(new[] { L.ConnectSavedWorld, L.ConnectInNewTab, L.Edit2, L.ScriptDuplicate, L.ExploreInDirectory, L.DeleteSavedWorld },
            menu.Items.OfType<MenuItem>().Select(m => m.Header as string));
        Assert.True(Item(menu, "ExploreWorldMenu").IsVisible);
        HelpCapture.Window(window, "saved-worlds-menu-System-Linen.png", Item(menu, "ConnectWorldMenu"));
        menu.Close(); PanelHeaderActionTests.Settle(window);

        // A world the directory does not list has no page to explore.
        var unlisted = OpenMenu(window, Row(list, "Emberwake"));
        Assert.False(Item(unlisted, "ExploreWorldMenu").IsVisible);
        // Duplicate makes a copy right after the world, selected, with the settings but not the saved password.
        Run(Item(unlisted, "DuplicateWorldMenu"));
        unlisted.Close(); PanelHeaderActionTests.Settle(window);
        var profiles = window.Controller.Settings.Profiles;
        var copy = Assert.Single(profiles, p => p.Name == L.Format(L.ScriptDuplicateName, "Emberwake"));
        Assert.NotEqual(UnlistedId, copy.Id);
        Assert.Equal((Unlisted.Host, Unlisted.Port), (copy.Host, copy.Port));
        Assert.Null(copy.PasswordId);
        Assert.Equal(profiles.ToList().FindIndex(p => p.Id == UnlistedId) + 1, profiles.ToList().FindIndex(p => p.Id == copy.Id));
        Assert.Equal(copy.Id, model.SelectedProfile?.Id);

        // Explore shows the world's page in Find a MUD.
        menu = OpenMenu(window, Row(list, "Legends of the Jedi"));
        Run(Item(menu, "ExploreWorldMenu"));
        menu.Close(); PanelHeaderActionTests.Settle(window);
        Assert.True(window.Sessions.IsBrowsing);
        var browser = window.Sessions.Browser(_catalog!);
        Assert.True(browser.IsExploring);
        Assert.Equal("legends-of-the-jedi", browser.SelectedWorld?.Id);

        // Edit opens the editor on that world; Delete asks first.
        menu = OpenMenu(window, Row(list, "Legends of the Jedi"));
        Run(Item(menu, "EditWorldMenu"));
        menu.Close(); PanelHeaderActionTests.Settle(window);
        var dialog = Assert.Single(window.OwnedWindows.OfType<ProfileDialog>());
        Assert.Equal("Legends of the Jedi", dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "WorldName").Text);
        dialog.Close(); PanelHeaderActionTests.Settle(window);
        menu = OpenMenu(window, Row(list, "Legends of the Jedi"));
        Run(Item(menu, "DeleteWorldMenu"));
        menu.Close(); PanelHeaderActionTests.Settle(window);
        Assert.True(model.ConfirmDelete);
        Assert.Equal("Legends of the Jedi", model.PendingDeleteName);
        Assert.Equal(3, window.Controller.Settings.Profiles.Count);
        await model.DeleteCommand.ExecuteAsync(null);
        Assert.DoesNotContain(window.Controller.Settings.Profiles, p => p.Id == ListedId);
    }

    [AvaloniaFact]
    public async Task DoubleClickAndEnterConnectAndConnectGoesBackToAnOpenSession()
    {
        var (window, view, _, _) = Open();
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "WorldProfiles");
        // Double-click a row.
        var row = Row(list, "Legends of the Jedi");
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        for (var i = 0; i < 2; i++) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
        await Settled(window);
        var legends = Assert.Single(window.Sessions.Tabs, t => t.Profile?.Id == ListedId);
        Assert.Same(legends, window.Sessions.Active);

        // Enter on another row opens that world beside it.
        Click(window, Row(list, "Emberwake"));
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settled(window);
        var ember = Assert.Single(window.Sessions.Tabs, t => t.Profile?.Id == UnlistedId);
        Assert.Same(ember, window.Sessions.Active);
        var count = window.Sessions.Tabs.Count;

        // Connecting to a world that already has a session goes to it instead of logging in twice.
        Click(window, Row(list, "Legends of the Jedi"));
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Settled(window);
        Assert.Equal(count, window.Sessions.Tabs.Count);
        Assert.Same(legends, window.Sessions.Active);

        // Connect in new tab always opens another.
        var menu = OpenMenu(window, Row(list, "Legends of the Jedi"));
        Run(Item(menu, "ConnectNewTabWorldMenu"));
        menu.Close();
        await Settled(window);
        Assert.Equal(count + 1, window.Sessions.Tabs.Count);
        Assert.Equal(2, window.Sessions.Tabs.Count(t => t.Profile?.Id == ListedId));
    }

    [AvaloniaFact]
    public void TheViewMenuTogglesSavedWorldsAndRestorePanelsBringsItBack()
    {
        var (window, _, _, _) = Open();
        var view = MenuItems(window, L.View);
        var item = view.Single(i => i.Header as string == L.SavedWorlds);
        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.True(item.IsChecked);
        Assert.Equal(L.Workspace, view.TakeWhile(i => i != item).Last().Header);
        item.Command!.Execute(null); PanelHeaderActionTests.Settle(window);
        Assert.False(window.IsSavedWorldsVisible);
        Assert.False(item.IsChecked);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<WorldLibraryView>(), v => v.IsEffectivelyVisible);
        // With the Workspace hidden too the left column leaves the layout, and comes back with its splitter.
        window.TogglePanel(); PanelHeaderActionTests.Settle(window);
        var layout = (IDock)window.Workspace.MapTool!.Owner!.Owner!.Owner!;
        Assert.DoesNotContain(layout.VisibleDockables!, d => d.Id == "left-column");
        item.Command!.Execute(null); PanelHeaderActionTests.Settle(window);
        Assert.True(window.IsSavedWorldsVisible);
        Assert.Equal("left-column", layout.VisibleDockables![0].Id);
        Assert.IsAssignableFrom<IProportionalDockSplitter>(layout.VisibleDockables[1]);
        Assert.False(window.IsPanelVisible());

        view.Single(i => i.Header as string == L.RestorePanels).Command!.Execute(null); PanelHeaderActionTests.Settle(window);
        Assert.True(window.IsSavedWorldsVisible && window.IsPanelVisible());
        Assert.Equal("saved-worlds-dock", ((IDock)window.Workspace.SavedWorldsTool!.Owner!).Id);
        Assert.Single(window.GetVisualDescendants().OfType<WorldLibraryView>());
    }

    [AvaloniaFact]
    public void ANarrowSavedWorldsHeaderPutsAddAndFindInARow()
    {
        var (window, _, _, chrome) = Open("Fleet", "Hull");
        var column = (IDockable)window.Workspace.SavedWorldsTool!.Owner!.Owner!;
        var share = column.Proportion;
        column.Proportion = .1; PanelHeaderActionTests.Settle(window);
        var header = PanelHeader.HostOf(chrome)!;
        Assert.True(header.IsInRow);
        DockDropPreviewTests.Capture(window, "saved-worlds-narrow-Fleet-Hull");
        // Wider than the default column, which the shrink above has renormalised.
        column.Proportion = share * 1.5; PanelHeaderActionTests.Settle(window);
        Assert.False(header.IsInRow);
    }

    private static ListBoxItem Row(ListBox list, string name) =>
        list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.Content is ConnectionProfile p && p.Name == name);

    private static ContextMenu OpenMenu(Window window, ListBoxItem row)
    {
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right);
        HelpCapture.Settle(window);
        var menu = row.ContextMenu!;
        Assert.True(menu.IsOpen);
        return menu;
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        PanelHeaderActionTests.Settle(window);
    }

    private static MenuItem Item(ContextMenu menu, string name) => menu.Items.OfType<MenuItem>().Single(m => m.Name == name);

    private static void Run(MenuItem item)
    {
        Assert.True(item.Command!.CanExecute(item.CommandParameter));
        item.Command.Execute(item.CommandParameter);
    }

    private static MenuItem[] MenuItems(MainWindow window, string group)
    {
        var menu = window.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
        return menu.Items.OfType<MenuItem>().Single(m => m.Header as string == group).Items.OfType<MenuItem>().ToArray();
    }

    private static async Task Settled(Window window)
    {
        for (var i = 0; i < 5; i++) { PanelHeaderActionTests.Settle(window); await Task.Delay(20); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_window is not null)
        {
            foreach (var owned in _window.OwnedWindows.ToArray()) owned.Close();
            await _window.Sessions.DisposeAsync(); _window.Close();
        }
        ThemeService.Apply(new());
        _listener.Stop();
        _catalog?.Dispose();
        try { Directory.Delete(_path, true); } catch (IOException) { }
    }
}
