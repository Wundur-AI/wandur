using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// A panel's actions in its title bar: declared by the panel's view, drawn between the title and the dock's own
/// buttons, right-aligned, reachable by keyboard and named for screen readers, and moved to one row under the header
/// when the header is too narrow to keep the title readable beside them.
/// </summary>
public sealed partial class PanelHeaderActionTests
{
    public static TheoryData<string, string> Looks => DockDropPreviewTests.Looks;

    private sealed partial class Probe : ObservableObject
    {
        [ObservableProperty] private bool _on;
        public int Clicks { get; private set; }
        public IRelayCommand Click => new RelayCommand(() => Clicks++);
    }

    private static (Probe, PanelHeaderAction[]) Actions()
    {
        var probe = new Probe();
        return (probe, [
            new PanelHeaderAction("ProbeToggle", "M 2,2 H 14 V 14 H 2 Z", nameof(L.MapRoomSearchToggle)) { Source = probe, CheckedPath = nameof(Probe.On) },
            new PanelHeaderAction("ProbeButton", "M 8,2 V 14 M 2,8 H 14", nameof(L.AddAWorld)) { Command = probe.Click },
        ]);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task ActionsSitRightAlignedBetweenTheTitleAndTheDockButtons(string skin, string theme)
    {
        var (window, chrome) = await Open(skin, theme);
        try
        {
            var (probe, actions) = Actions();
            PanelHeader.SetActions(Content(chrome), actions);
            Settle(window);

            var host = PanelHeader.HostOf(chrome)!;
            Assert.False(host.IsInRow);
            var toggle = Assert.IsType<ToggleButton>(Named(chrome, "ProbeToggle"));
            var button = Named(chrome, "ProbeButton");
            var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            var dock = chrome.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_MenuButton").GetVisualParent()!;
            var header = chrome.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip");
            Rect At(Visual v) => new(v.TranslatePoint(default, window)!.Value, v.Bounds.Size);
            Assert.True(At(title).Right <= At(toggle).Left, $"title {At(title)} before the actions {At(toggle)}");
            Assert.True(At(toggle).Right <= At(button).Left);
            // Right-aligned: the actions end where the dock's buttons begin, or, where those show only on hover (System and
            // Fleet), at the header's right edge, the dock's buttons opening between the title and the actions.
            var grid = (Visual)header.GetVisualChildren().Single();
            if (PanelHeader.DockButtonsOnHover(chrome)) Assert.InRange(At(grid).Right - At(button).Right, 0, 4);
            else Assert.InRange(At(dock).Left - At(button).Right, 0, 4);
            // Inside the header band, centred in it.
            Assert.InRange(Math.Abs(At(button).Center.Y - At(header).Center.Y), 0, 1.5);
            Assert.All(new[] { toggle, button }, b => Assert.True(b.IsEffectivelyVisible && b.Focusable && b.IsTabStop));
            // The size of the dock's own header buttons.
            Assert.All(new[] { toggle, button }, b => Assert.Equal(new Size(24, 24), b.Bounds.Size));
            Assert.Equal(L.MapRoomSearchToggle, ToolTip.GetTip(toggle));
            Assert.Equal(L.AddAWorld, Avalonia.Automation.AutomationProperties.GetName(button));

            button.Command!.Execute(null);
            Assert.Equal(1, probe.Clicks);
            toggle.IsChecked = true;
            Assert.True(probe.On);
            probe.On = false;
            Assert.False(toggle.IsChecked);

            // Tab goes from the actions to the dock's buttons.
            toggle.Focus(NavigationMethod.Tab);
            Tab(window);
            Assert.Same(button, window.FocusManager!.GetFocusedElement());
            Tab(window);
            Assert.Equal("PART_MenuButton", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
            DockDropPreviewTests.Capture(window, $"header-actions-{skin}-{theme}");
        }
        finally { await Close(window); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task ANarrowHeaderMovesTheActionsToARowUnderItAndWideningBringsThemBack(string skin, string theme)
    {
        var (window, chrome) = await Open(skin, theme);
        try
        {
            var (_, actions) = Actions();
            PanelHeader.SetActions(Content(chrome), actions);
            Settle(window);
            var host = PanelHeader.HostOf(chrome)!;
            Assert.False(host.IsInRow);
            var bodyTop = Body(chrome).TranslatePoint(default, chrome)!.Value.Y;

            var column = (IDockable)window.Workspace.ChannelsTool!.Owner!.Owner!;
            var share = column.Proportion;
            // Narrower where the dock's buttons show only on hover: hidden, they leave the actions their room.
            column.Proportion = PanelHeader.DockButtonsOnHover(chrome) ? .06 : .1; Settle(window);
            Assert.True(host.IsInRow, $"header {Header(chrome).Bounds.Width} wide");
            var row = chrome.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PanelHeaderActionRow");
            Assert.True(row.IsEffectivelyVisible);
            var button = Named(chrome, "ProbeButton");
            Assert.Same(row, button.FindAncestorOfType<Border>());
            var header = chrome.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Border" && b.TemplatedParent == chrome);
            // Directly under the header, and the panel's body starts under the row.
            Assert.InRange(row.TranslatePoint(default, chrome)!.Value.Y - (header.TranslatePoint(default, chrome)!.Value.Y + header.Bounds.Height), -0.5, 0.5);
            Assert.InRange(Body(chrome).TranslatePoint(default, chrome)!.Value.Y - (bodyTop + row.Bounds.Height), -1, 1);
            // The title keeps at least its first characters.
            var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            Assert.True(title.Bounds.Width > 30, $"title {title.Bounds.Width}");
            Assert.True(button.IsEffectivelyVisible && button.Focusable);
            DockDropPreviewTests.Capture(window, $"header-actions-narrow-{skin}-{theme}");

            column.Proportion = share; Settle(window);
            Assert.False(host.IsInRow);
            Assert.False(row.IsVisible);
            Assert.Equal("PanelHeaderActions", Named(chrome, "ProbeButton").GetVisualParent<Control>()?.Name);
            Assert.InRange(Body(chrome).TranslatePoint(default, chrome)!.Value.Y, bodyTop - 1, bodyTop + 1);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task AFloatingPanelKeepsItsActionsAndAClearedDeclarationRemovesThem()
    {
        var (window, chrome) = await Open("System", "Midnight");
        try
        {
            var (_, actions) = Actions();
            var tool = window.Workspace.ChannelsTool!;
            window.Workspace.FloatDockable(tool);
            Settle(window);
            var root = window.Workspace.FindRoot(tool, _ => true)!;
            var floating = (Window)root.Window!.Host!;
            DockDropPreviewTests.Settle(floating);
            var floated = floating.GetVisualDescendants().OfType<ToolChromeControl>().Single(c => c.IsEffectivelyVisible);
            // The floating window builds the panel's view again; a view declares its actions as it is built.
            var content = Content(floated);
            PanelHeader.SetActions(content, actions);
            DockDropPreviewTests.Settle(floating);
            Assert.NotNull(Named(floated, "ProbeToggle"));
            Assert.True(Named(floated, "ProbeButton").IsEffectivelyVisible);

            PanelHeader.SetActions(content, null);
            DockDropPreviewTests.Settle(floating);
            Assert.DoesNotContain(floated.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("panel-action"));
            floating.Close();
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task AHiddenViewGivesUpTheHeaderToTheShownOne()
    {
        var (window, chrome) = await Open("Fleet", "Hull");
        try
        {
            var (_, actions) = Actions();
            var content = Content(chrome);
            PanelHeader.SetActions(content, actions);
            Settle(window);
            Assert.NotEmpty(PanelHeader.HostOf(chrome)!.Buttons);
            content.IsVisible = false;
            Settle(window);
            Assert.Empty(PanelHeader.HostOf(chrome)!.Buttons);
            content.IsVisible = true;
            Settle(window);
            Assert.Equal(2, PanelHeader.HostOf(chrome)!.Buttons.Count);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public void AViewOutsideADockPanelShowsItsActionsInItsOwnBar()
    {
        var (probe, actions) = Actions();
        var view = new Border();
        var bar = new PanelActionBar(view);
        view.Child = bar;
        PanelHeader.SetActions(view, actions);
        var window = new Window { Content = view, Width = 300, Height = 200 };
        try
        {
            window.Show(); DockDropPreviewTests.Settle(window);
            Assert.True(bar.IsVisible && bar.HasActions);
            var button = Named(bar, "ProbeButton");
            Assert.DoesNotContain("panel-action", button.Classes);
            button.Command!.Execute(null);
            Assert.Equal(1, probe.Clicks);
            Assert.IsType<ToggleButton>(Named(bar, "ProbeToggle"));
            PanelHeader.SetActions(view, null);
            Assert.False(bar.IsVisible);
        }
        finally { window.Close(); }
    }

    internal static void Tab(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        DockDropPreviewTests.Settle(window);
    }

    internal static Button Named(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static Control Content(ToolChromeControl chrome) =>
        chrome.GetVisualDescendants().OfType<Wandur.Desktop.Views.ActiveSessionView>().First();

    private static Control Body(ToolChromeControl chrome) =>
        chrome.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_ContentPresenter" && c.TemplatedParent == chrome);

    private static Grid Header(ToolChromeControl chrome) =>
        (Grid)chrome.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip").GetVisualChildren().Single();

    internal static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) DockDropPreviewTests.Settle(window);
    }

    private static Task<(MainWindow, ToolChromeControl)> Open(string skin, string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        window.Width = 1440; window.Height = 900;
        window.Show(); Settle(window);
        window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = theme });
        Settle(window);
        return Task.FromResult((window, new DockDropPreviewTests.DockDrag(window).Panel("channels")));
    }

    private static async Task Close(MainWindow window)
    {
        await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new());
    }
}
