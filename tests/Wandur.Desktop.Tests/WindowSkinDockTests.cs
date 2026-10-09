using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

public sealed class WindowSkinDockTests
{
    [AvaloniaTheory]
    [InlineData("Fleet", 2, 30)]
    [InlineData("Armored", 1, 30)]
    [InlineData("System", 0, 30)]
    public async Task PanelsUseSelectedMetricsWithoutRebuildingDockTree(string skin, double rim, double header)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            var worlds = window.Workspace.WorldsTool;
            window.Sessions.PreviewAppearanceSettings(new() { Skin = skin });
            WindowSkinTransitionTests.Settle(window);
            Assert.Same(worlds, window.Workspace.WorldsTool);
            var docks = window.GetVisualDescendants().OfType<ThemeDockSkinHost>().ToArray();
            Assert.NotEmpty(docks);
            foreach (var dock in docks)
            {
                Assert.Equal(rim, dock.Child!.Bounds.Top);
                Assert.Equal(header, dock.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip").Bounds.Height);
            }
            var left = docks.Single(d => d.DataContext is IToolDock { Id: "left" });
            var right = docks.Single(d => d.DataContext is IToolDock { Id: "map-dock" });
            Assert.Equal(0, left.Child!.Bounds.Left);
            Assert.Equal(right.Bounds.Width, right.Child!.Bounds.Right);
            foreach (var (showLeft, showRight) in new[] { (false, true), (true, false), (false, false), (true, true) })
            {
                if (window.IsPanelVisible() != showLeft) window.TogglePanel();
                if (window.IsSavedWorldsVisible != showLeft) window.ToggleSavedWorlds();
                if (window.IsMapVisible != showRight) window.ToggleMap();
                if (window.IsChannelsVisible != showRight) window.ToggleChannels();
                WindowSkinTransitionTests.Settle(window);
                Assert.Same(worlds, window.Workspace.WorldsTool);
                var visible = window.GetVisualDescendants().OfType<ThemeDockSkinHost>()
                    .Where(d => d.IsEffectivelyVisible && d.Bounds.Width > 6 && d.Bounds.Height > 6).ToArray();
                Assert.Equal((showLeft ? 2 : 0) + (showRight ? 2 : 0), visible.Length);
                foreach (var dock in visible)
                {
                    Assert.Equal(rim, dock.Child!.Bounds.Top);
                    if (dock.DataContext is IToolDock { Id: "left" or "saved-worlds-dock" }) Assert.Equal(0, dock.Child.Bounds.Left);
                    else Assert.Equal(dock.Bounds.Width, dock.Child.Bounds.Right);
                }
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public async Task FloatingWindowsFollowEverySkinPairAndUnsubscribeWhenClosed()
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            window.Workspace.FloatDockable(window.Workspace.WorldsTool!);
            WindowSkinTransitionTests.Settle(window);
            var floating = Assert.Single(window.Workspace.HostWindows.OfType<Window>());
            Assert.IsType<SkinnedDockHostWindow>(floating);
            var content = floating.Content;
            foreach (var first in WindowSkinId.All)
            foreach (var second in WindowSkinId.All)
            {
                window.Sessions.PreviewAppearanceSettings(new() { Skin = first });
                window.Sessions.PreviewAppearanceSettings(new() { Skin = second });
                WindowSkinTransitionTests.Settle(floating);
                Assert.Same(content, floating.Content);
                Assert.Equal(WindowDecorations.Full, floating.WindowDecorations);
                Assert.False(floating.ExtendClientAreaToDecorationsHint);
                Assert.Contains("skin-" + second.ToLowerInvariant(), floating.Classes);
                Assert.False(string.IsNullOrWhiteSpace(floating.Title));
                var dock = Assert.Single(floating.GetVisualDescendants().OfType<ThemeDockSkinHost>());
                Assert.Equal(second == "System" ? 0 : second == "Armored" ? 1 : 2, dock.Child!.Bounds.Left);
                Assert.Contains(dock.GetVisualDescendants().OfType<Button>(), b => b.Name == "PART_CloseButton");
            }
            // Opened fires again for an existing host after Hide/Show.
            floating.Hide(); floating.Show(); WindowSkinTransitionTests.Settle(floating);
            floating.Hide(); floating.Show(); WindowSkinTransitionTests.Settle(floating);
            window.ResetLayout(); WindowSkinTransitionTests.Settle(window);
            var closedClasses = floating.Classes.ToArray();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = "Armored" });
            Assert.Equal(closedClasses, floating.Classes.ToArray());
            window.Workspace.FloatDockable(window.Workspace.WorldsTool!);
            WindowSkinTransitionTests.Settle(window);
            var reopened = Assert.Single(window.Workspace.HostWindows.OfType<Window>());
            Assert.NotSame(floating, reopened);
            Assert.Contains("skin-armored", reopened.Classes);
            window.ResetLayout();
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }
}
