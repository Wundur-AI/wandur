using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;

namespace Wandur.Desktop.Tests;

public sealed class ArmoredDockTests
{
    private static T Find<T>(Control host, string name) where T : Control =>
        host.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static ThemeDockSkinHost Dock(Window window, string id) =>
        window.GetVisualDescendants().OfType<ThemeDockSkinHost>().Single(d => d.DataContext is IToolDock dock && dock.Id == id);
    private static void Settle(Window window) => WindowSkinTransitionTests.Settle(window);

    [AvaloniaFact]
    public async Task BayCornersStaySquareAfterRepeatedPaletteAndSkinSwitches()
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); Settle(window);
            foreach (var theme in new[] { "Hull", "Slate", "Paper" })
            {
                window.Sessions.PreviewAppearanceSettings(new() { Skin = "Fleet", Theme = theme }); Settle(window);
                var borders = Dock(window, "map-dock").GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Name == "PART_Border" && b.TemplatedParent is ToolChromeControl or ToolControl).ToArray();
                Assert.Equal(2, borders.Length);
                var fleetCorners = borders.Select(b => b.CornerRadius).ToArray();
                window.Sessions.PreviewAppearanceSettings(new() { Skin = "Armored", Theme = theme }); Settle(window);
                Assert.All(borders, b => Assert.Equal(default(CornerRadius), b.CornerRadius));
                window.Sessions.PreviewAppearanceSettings(new() { Skin = "Fleet", Theme = theme }); Settle(window);
                Assert.Equal(fleetCorners, borders.Select(b => b.CornerRadius).ToArray());
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public async Task JoinedBayEdgesStayFlushAndFleetCornersReturnAfterArmoredStartup()
    {
        var store = new Wandur.Core.Settings.SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-bay-" + Guid.NewGuid(), "settings.json"));
        store.Save(new() { Skin = "Armored", Theme = "Hull" });
        var window = new MainWindow(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store,
            new MemoryPasswordVault(), new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());
        try
        {
            window.Show(); Settle(window);
            Assert.Equal(0, Find<Border>(Dock(window, "left"), "ArmoredBayRim").BorderThickness.Left);
            Assert.Equal(0, Find<Border>(Dock(window, "map-dock"), "ArmoredBayRim").BorderThickness.Right);
            window.Sessions.PreviewAppearanceSettings(new() { Skin = "Fleet", Theme = "Hull" }); Settle(window);
            var header = Dock(window, "map-dock").GetVisualDescendants().OfType<Border>()
                .Single(b => b.Name == "PART_Border" && b.TemplatedParent is ToolChromeControl);
            Assert.True(header.CornerRadius.TopRight > 0, "Starting in Armored must not erase Fleet's outer corner geometry.");
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaTheory]
    [InlineData("Hull")]
    [InlineData("Slate")]
    [InlineData("Paper")]
    public async Task ArmoredBaysHaveCompactIntegratedHeadersAndKeepContentReadable(string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); await window.Controller.StartAsync();
            var controller = window.Controller;
            window.Sessions.PreviewAppearanceSettings(new() { Skin = "Armored", Theme = theme });
            foreach (var width in new[] { 1040, 1536 })
            foreach (var scale in new[] { 1d, 2d })
            {
                window.Width = width; window.Height = 900; window.SetRenderScaling(scale); Settle(window);
                foreach (var (id, bar) in new[] { ("map-dock", "MapToolbar"), ("channels-dock", "ChannelsToolbar") })
                {
                    var dock = Dock(window, id);
                    var grip = Find<Grid>(dock, "PART_Grip");
                    var toolbar = Find<Border>(dock, bar);
                    Assert.InRange(grip.Bounds.Height, 28, 30);
                    Assert.Same(grip.Background, toolbar.Background);
                    Assert.IsAssignableFrom<ISolidColorBrush>(toolbar.Background);
                    var title = Find<TextBlock>(dock, "PART_Title");
                    var close = Find<Button>(dock, "PART_CloseButton");
                    Assert.InRange(title.FontSize, 12, 13);
                    Assert.True(title.TranslatePoint(new Point(title.Bounds.Width, 0), window)!.Value.X <= close.TranslatePoint(default, window)!.Value.X);
                    var overlay = Find<Grid>(dock, "ArmoredBayChrome");
                    Assert.True(overlay.IsEffectivelyVisible);
                    Assert.False(overlay.IsHitTestVisible);
                }
                Assert.Empty(ContrastProbe.Scan(window));
                Assert.Same(controller, window.Controller);
                Capture(window, $"armored-bays-{theme}-{width}-{scale}x");
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public async Task FocusMarkerMovesBetweenBaysAndDoesNotInterceptClose()
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); window.Sessions.PreviewAppearanceSettings(new() { Skin = "Armored" }); Settle(window);
            var root = Find<DockControl>(window, "WorkspaceDock").Layout!;
            var map = Dock(window, "map-dock"); var channels = Dock(window, "channels-dock");
            var mapMark = Find<Border>(map, "ArmoredBayFocus");
            var channelMark = Find<Border>(channels, "ArmoredBayFocus");
            window.Workspace.SetFocusedDockable(root, window.Workspace.MapTool!); Settle(window);
            Assert.True(mapMark.IsEffectivelyVisible); Assert.False(channelMark.IsEffectivelyVisible);
            Assert.InRange(mapMark.Bounds.Width, 24, 36);
            Assert.InRange(mapMark.Bounds.Height, 1, 2);
            Capture(window, "armored-bay-map-focused");
            window.Workspace.SetFocusedDockable(root, window.Workspace.ChannelsTool!); Settle(window);
            Assert.False(mapMark.IsEffectivelyVisible); Assert.True(channelMark.IsEffectivelyVisible);
            var close = Find<Button>(map, "PART_CloseButton");
            var at = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window);
            Assert.False(window.IsMapVisible);
            Assert.True(window.IsChannelsVisible);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public async Task BayStylesFollowFloatingPanelsAndReleaseOnOtherSkins()
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); Settle(window);
            foreach (var skin in new[] { "Fleet", "System" })
            {
                window.Sessions.PreviewAppearanceSettings(new() { Skin = skin }); Settle(window);
                var baselineFont = Find<TextBlock>(Dock(window, "map-dock"), "PART_Title").FontSize;
                window.Sessions.PreviewAppearanceSettings(new() { Skin = "Armored" }); Settle(window);
                window.Workspace.FloatDockable(window.Workspace.MapTool!); Settle(window);
                var floating = Assert.Single(window.Workspace.HostWindows.OfType<Window>()); Settle(floating);
                var content = floating.Content;
                Assert.True(Find<Grid>(floating, "ArmoredBayChrome").IsEffectivelyVisible);
                Assert.Same(Find<Grid>(floating, "PART_Grip").Background, Find<Border>(floating, "MapToolbar").Background);
                Capture(floating, "armored-bay-floating-from-" + skin);
                window.Sessions.PreviewAppearanceSettings(new() { Skin = skin }); Settle(floating);
                Assert.False(Find<Grid>(floating, "ArmoredBayChrome").IsEffectivelyVisible);
                Assert.Equal(baselineFont, Find<TextBlock>(floating, "PART_Title").FontSize);
                Assert.Same(Application.Current!.Resources["ChromeBrush"], Find<Border>(floating, "MapToolbar").Background);
                Assert.Same(content, floating.Content);
                Assert.Equal(WindowDecorations.Full, floating.WindowDecorations);
                window.ResetLayout(); Settle(window);
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
