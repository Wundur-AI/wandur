using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Wandur.Core.Mapping;
using Wandur.Desktop.ViewModels;
using Wandur.Desktop.Views;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// The Map panel's search, auto-centre, fit, tools and Stop are actions in its title bar, so the map takes the height
/// the toolbar row had; the search box still opens as a row over the map, and the editor keeps its own toolbar.
/// </summary>
public sealed class MapHeaderActionTests
{
    private static readonly string[] Names = ["MapSearchToggle", "MapAutoCenterToggle", "FitMapFloor", "MapToolsToggle"];

    [AvaloniaTheory]
    [MemberData(nameof(DockDropPreviewTests.Looks), MemberType = typeof(DockDropPreviewTests))]
    public async Task TheMapsActionsAreInItsTitleBarAndTheMapHasTheToolbarsHeight(string skin, string theme)
    {
        var (window, chrome, view) = Open(skin, theme);
        try
        {
            var header = PanelHeader.HostOf(chrome)!;
            Assert.False(header.IsInRow);
            Assert.Equal(Names, header.Buttons.Where(b => b.IsVisible).Select(b => b.Name));
            Assert.All(header.Buttons, b => Assert.Same(header.HeaderPanel, b.GetVisualParent()));
            Assert.Equal(L.MapRoomSearchToggle, ToolTip.GetTip(header.Buttons[0]));
            // No toolbar row in the panel: the map starts at the top of the panel's body.
            var toolbar = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MapToolbar");
            Assert.False(toolbar.IsVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => Names.Contains(b.Name));
            var canvas = view.GetVisualDescendants().OfType<RoomMapControl>().Single();
            Assert.InRange(canvas.TranslatePoint(default, view)!.Value.Y, 0, .5);
            // Stop shows only while walking.
            var stop = PanelHeaderActionTests.Named(chrome, "MapStopWalkingToolbar");
            Assert.False(stop.IsVisible);
            Assert.Same(view.Model.StopWalkingCommand, stop.Command);
            DockDropPreviewTests.Capture(window, $"map-header-{skin}-{theme}");
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task TheHeaderTogglesDriveTheMapAndSearchOpensItsRow()
    {
        var (window, chrome, view) = Open("System", "Linen");
        try
        {
            var model = view.Model;
            var tools = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MapToolsPanel");
            var toolsToggle = Assert.IsType<ToggleButton>(PanelHeaderActionTests.Named(chrome, "MapToolsToggle"));
            toolsToggle.IsChecked = true; PanelHeaderActionTests.Settle(window);
            Assert.True(model.IsToolsOpen);
            Assert.True(tools.IsEffectivelyVisible);
            toolsToggle.IsChecked = false; PanelHeaderActionTests.Settle(window);
            Assert.False(tools.IsVisible);

            var center = Assert.IsType<ToggleButton>(PanelHeaderActionTests.Named(chrome, "MapAutoCenterToggle"));
            Assert.Equal(model.AutoCenter, center.IsChecked);
            center.IsChecked = !model.AutoCenter;
            Assert.Equal(center.IsChecked, model.AutoCenter);
            Assert.Same(model.FitFloorCommand, PanelHeaderActionTests.Named(chrome, "FitMapFloor").Command);

            var search = Assert.IsType<ToggleButton>(PanelHeaderActionTests.Named(chrome, "MapSearchToggle"));
            search.IsChecked = true; PanelHeaderActionTests.Settle(window);
            Assert.True(model.IsRoomSearchVisible);
            var toolbar = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MapToolbar");
            var box = view.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "MapSearchBox");
            Assert.True(toolbar.IsVisible && box.IsEffectivelyVisible);
            Assert.InRange(toolbar.TranslatePoint(default, view)!.Value.Y, 0, .5);
            DockDropPreviewTests.Capture(window, "map-header-search-System-Linen");
            search.IsChecked = false; PanelHeaderActionTests.Settle(window);
            Assert.False(toolbar.IsVisible);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task ANarrowMapPanelPutsItsActionsInARowUnderTheHeader()
    {
        var (window, chrome, _) = Open("Fleet", "Hull");
        try
        {
            var column = (IDockable)window.Workspace.MapTool!.Owner!.Owner!;
            var share = column.Proportion;
            column.Proportion = .1; PanelHeaderActionTests.Settle(window);
            var header = PanelHeader.HostOf(chrome)!;
            Assert.True(header.IsInRow);
            Assert.All(header.Buttons, b => Assert.Same(header.Row, b.FindAncestorOfType<Border>()));
            DockDropPreviewTests.Capture(window, "map-header-narrow-Fleet-Hull");
            column.Proportion = share; PanelHeaderActionTests.Settle(window);
            Assert.False(header.IsInRow);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public void AMapOutsideAPanelAndTheEditorKeepTheirToolbar()
    {
        foreach (var editing in new[] { false, true })
        {
            var view = new MapView(new MapViewModel(new RoomMapTracker()), editingWorkspace: editing);
            var window = new Window { Content = view, Width = 600, Height = 500 };
            try
            {
                window.Show(); PanelHeaderActionTests.Settle(window);
                var toolbar = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MapToolbar");
                Assert.True(toolbar.IsVisible);
                var names = toolbar.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).Select(b => b.Name).ToArray();
                Assert.Equal(editing ? Names.Where(n => n != "MapToolsToggle") : Names, names.Where(n => Names.Contains(n)));
            }
            finally { window.Close(); }
        }
    }

    private static (MainWindow, ToolChromeControl, MapView) Open(string skin, string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        window.Width = 1440; window.Height = 900;
        window.Show(); PanelHeaderActionTests.Settle(window);
        window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = theme });
        PanelHeaderActionTests.Settle(window);
        var chrome = new DockDropPreviewTests.DockDrag(window).Panel("map");
        var view = chrome.GetVisualDescendants().OfType<MapView>().Single(v => v.IsEffectivelyVisible);
        return (window, chrome, view);
    }

    private static async Task Close(MainWindow window)
    {
        await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new());
    }
}
