using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// In System and Fleet a panel's collapse, pin and close buttons show only while the pointer is over its header or
/// the keyboard is in it, so the header's actions sit against the right edge the rest of the time. Hidden, the buttons
/// stay in the tab order and the automation tree. Armored keeps them shown.
/// </summary>
public sealed class DockButtonsOnHoverTests
{
    [AvaloniaTheory]
    [InlineData("System", "Linen")]
    [InlineData("System", "Midnight")]
    [InlineData("Fleet", "Hull")]
    public async Task TheDockButtonsShowOnHoverAndTheActionsTakeTheirPlaceOtherwise(string skin, string theme)
    {
        var (window, chrome) = Open(skin, theme);
        try
        {
            var strip = Strip(chrome);
            var header = chrome.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip");
            window.MouseMove(new Point(700, 450)); PanelHeaderActionTests.Settle(window);
            Assert.Equal(0, strip.Opacity);
            Assert.Equal(0, strip.Bounds.Width);
            var actions = PanelHeader.HostOf(chrome)!;
            var last = actions.Buttons.Last(b => b.IsVisible);
            var grid = (Visual)strip.GetVisualParent()!;
            // The actions end at the header's right edge while the dock's buttons are away.
            Assert.InRange(grid.Bounds.Width - (last.TranslatePoint(new Point(last.Bounds.Width, 0), grid)!.Value.X), 0, 4);
            DockDropPreviewTests.Capture(window, $"dock-buttons-away-{skin}-{theme}");

            var first = actions.Buttons.First(b => b.IsVisible);
            var before = first.TranslatePoint(default, window)!.Value;
            window.MouseMove(header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value);
            PanelHeaderActionTests.Settle(window);
            Assert.Equal(1, strip.Opacity);
            Assert.True(strip.Bounds.Width >= 70, $"strip {strip.Bounds.Width}");
            // The dock's buttons open between the title and the actions; the actions stay where the pointer expects them.
            Assert.Equal(before, first.TranslatePoint(default, window)!.Value);
            Assert.True(strip.Bounds.Right <= first.TranslatePoint(default, grid)!.Value.X + 1);
            Assert.False(actions.IsInRow);
            DockDropPreviewTests.Capture(window, $"dock-buttons-hover-{skin}-{theme}");

            window.MouseMove(new Point(700, 450)); PanelHeaderActionTests.Settle(window);
            Assert.Equal(0, strip.Opacity);
        }
        finally { await Close(window); }
    }

    [AvaloniaTheory]
    [InlineData("System", "Linen")]
    [InlineData("Fleet", "Hull")]
    public async Task TheHiddenButtonsAreReachedByTabAndNamedForScreenReaders(string skin, string theme)
    {
        var (window, chrome) = Open(skin, theme);
        try
        {
            var strip = Strip(chrome);
            Assert.Equal(0, strip.Opacity);
            var close = chrome.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CloseButton");
            // Not removed: still visible to layout, focus and automation, only transparent and zero-width.
            Assert.True(close.IsEffectivelyVisible && close.IsEffectivelyEnabled && close.Focusable);
            var peer = ControlAutomationPeer.CreatePeerForElement(close);
            Assert.Equal(L.DockClosePanel, peer.GetName());
            Assert.True(peer.IsEnabled());

            // From the last header action, Tab reaches the dock's buttons, and they show while they have focus.
            var last = PanelHeader.HostOf(chrome)!.Buttons.Last(b => b.IsVisible);
            last.Focus(NavigationMethod.Tab);
            PanelHeaderActionTests.Tab(window);
            Assert.Equal("PART_MenuButton", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
            PanelHeaderActionTests.Settle(window);
            Assert.Equal(1, strip.Opacity);
            Assert.True(strip.Bounds.Width >= 70);
            PanelHeaderActionTests.Tab(window);
            Assert.Equal("PART_PinButton", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
            PanelHeaderActionTests.Tab(window);
            Assert.Same(close, window.FocusManager!.GetFocusedElement());
            Assert.Equal(1, strip.Opacity);
            DockDropPreviewTests.Capture(window, $"dock-buttons-focus-{skin}-{theme}");

            // Space on the focused close button closes the panel, as it always did.
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            PanelHeaderActionTests.Settle(window);
            Assert.False(window.IsMapVisible);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task ArmoredKeepsItsButtonsShown()
    {
        var (window, chrome) = Open("Armored", "Hull");
        try
        {
            window.MouseMove(new Point(700, 450)); PanelHeaderActionTests.Settle(window);
            var strip = Strip(chrome);
            Assert.Equal(1, strip.Opacity);
            Assert.True(strip.Bounds.Width >= 70);
            Assert.False(PanelHeader.DockButtonsOnHover(chrome));
        }
        finally { await Close(window); }
    }

    private static Panel Strip(ToolChromeControl chrome) =>
        chrome.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains("dock-buttons"));

    private static (MainWindow, ToolChromeControl) Open(string skin, string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        window.Width = 1440; window.Height = 900;
        window.Show(); PanelHeaderActionTests.Settle(window);
        window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = theme });
        PanelHeaderActionTests.Settle(window);
        return (window, new DockDropPreviewTests.DockDrag(window).Panel("map"));
    }

    private static async Task Close(MainWindow window)
    {
        await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new());
    }
}
