using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using SkiaSharp;

namespace Wandur.Desktop.Tests;

/// <summary>
/// Dragging a panel works the way Visual Studio's does: themed guides over the panel under the pointer and at the edges
/// of the window, and a translucent preview in the theme's accent of exactly the rectangle the panel will take if it is
/// released now (half a panel, a whole panel as a tab, a strip of the window, or the floating window). Releasing on a
/// guide docks the panel there. With WANDUR_CAPTURE_DIR set, every case also saves a mid-drag screenshot.
/// </summary>
public sealed class DockDropPreviewTests
{
    public static TheoryData<string, string> Looks => new()
    {
        { "System", "Linen" }, { "System", "Midnight" }, { "Fleet", "Hull" }, { "Armored", "Hull" },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task DockingLeftOfTheMapPreviewsItsLeftHalfAndDocksThere(string skin, string theme)
    {
        var (window, drag) = Open(skin, theme);
        try
        {
            drag.Press(drag.Header("channels"));
            drag.Move(drag.Centre(drag.Panel("map")));
            var target = drag.LocalTarget();
            drag.Move(drag.Centre(Part<Control>(target, "PART_LeftSelector")));
            var map = drag.Panel("map");
            var preview = AssertPreview(window, target, "PART_LeftIndicator");
            AssertRect(Half(drag.Bounds(map.FindAncestorOfType<DockableControl>()!), DockOperation.Left), drag.Bounds(preview), 3);
            Assert.True(Part<DockGuide>(target, "PART_LeftSelector").IsLit);
            // On a guide the floating preview gives way to the drop preview.
            Assert.Equal("Dock", DragPreview().Control.Status);
            Assert.Equal(0, DragPreview().Control.Opacity);
            Assert.False(Part<DockGuide>(target, "PART_RightSelector").IsLit);
            Capture(window, $"left-of-map-{skin}-{theme}");
            drag.Release();

            var channels = drag.Bounds(drag.Panel("channels"));
            var after = drag.Bounds(drag.Panel("map"));
            Assert.True(channels.Right <= after.Left + 1, $"channels {channels} sits left of the map {after}");
            Assert.InRange(Math.Abs(channels.Top - after.Top), 0, 1);
            // Channels leaves the column it shared with the map, so the map's dock grows into that space before it is
            // split; the split itself is the even one the preview showed.
            Assert.InRange(Math.Abs(channels.Height - after.Height), 0, 1);
            Assert.InRange(channels.Width / (channels.Width + after.Width), .45, .55);
            AssertLayoutStillWorks(window);
        }
        finally { await Close(window); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task DockingAsATabPreviewsTheWholePanelAndJoinsItsTabs(string skin, string theme)
    {
        var (window, drag) = Open(skin, theme);
        try
        {
            drag.Press(drag.Header("map"));
            var channelsPanel = drag.Panel("channels");
            drag.Move(drag.Centre(channelsPanel));
            var target = drag.LocalTarget();
            drag.Move(drag.Centre(Part<Control>(target, "PART_CenterSelector")));
            var preview = AssertPreview(window, target, "PART_CenterIndicator");
            AssertRect(drag.Bounds(channelsPanel.FindAncestorOfType<DockableControl>()!), drag.Bounds(preview), 3);
            Capture(window, $"tab-with-channels-{skin}-{theme}");
            drag.Release();

            var map = window.Workspace.MapTool!;
            var channels = window.Workspace.ChannelsTool!;
            Assert.Same(map.Owner, channels.Owner);
            Assert.Equal(2, ((IDock)map.Owner!).VisibleDockables!.Count);
            AssertLayoutStillWorks(window);
        }
        finally { await Close(window); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task DockingToTheWindowBottomPreviewsAStripAndDocksAtThatSize(string skin, string theme)
    {
        var (window, drag) = Open(skin, theme);
        try
        {
            drag.Press(drag.Header("map"));
            var dock = window.GetVisualDescendants().OfType<DockControl>().Single(d => d.Name == "WorkspaceDock");
            drag.Move(drag.Centre(dock));
            var target = window.GetVisualDescendants().OfType<GlobalDockTarget>().Single();
            drag.Move(drag.Centre(Part<Control>(target, "PART_BottomSelector")));
            var preview = AssertPreview(window, target, "PART_BottomIndicator");
            var area = drag.Bounds(dock);
            var expected = drag.Bounds(preview);
            Assert.InRange(expected.Height / area.Height, .2, .3);
            Assert.InRange(Math.Abs(expected.Bottom - area.Bottom), 0, 2);
            Assert.InRange(Math.Abs(expected.Width - area.Width), 0, 3);
            Capture(window, $"window-bottom-{skin}-{theme}");
            drag.Release();

            var map = drag.Bounds(drag.Panel("map").FindAncestorOfType<DockableControl>()!);
            AssertRect(expected, map, 8);
            AssertLayoutStillWorks(window);
        }
        finally { await Close(window); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public async Task FloatingPreviewsTheWindowItWillOpenAndReleasingFloatsIt(string skin, string theme)
    {
        var (window, drag) = Open(skin, theme);
        try
        {
            var mapPanel = drag.Panel("map");
            var size = mapPanel.FindAncestorOfType<DockableControl>()!.Bounds.Size;
            var corner = window.PointToScreen(drag.Bounds(mapPanel.FindAncestorOfType<DockableControl>()!).TopLeft);
            var start = drag.Header("map");
            drag.Press(start);
            // Over the transcript, away from every guide: nothing to dock to, so the panel would float.
            var dock = window.GetVisualDescendants().OfType<DockControl>().Single(d => d.Name == "WorkspaceDock");
            var at = drag.Centre(dock) + new Vector(-160, 140);
            drag.Move(at);
            foreach (var target in window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DockTargetBase>())
            foreach (var name in new[] { "PART_LeftIndicator", "PART_RightIndicator", "PART_TopIndicator", "PART_BottomIndicator" })
                Assert.Equal(0, Part<Control>(target, name).Opacity);
            var (previewWindow, control) = DragPreview();
            Assert.True(previewWindow.IsVisible);
            Assert.Equal("Float", control.Status);
            var ghost = Part<Border>(control, "PART_Ghost");
            Assert.True(ghost.IsEffectivelyVisible);
            Assert.Equal(size.Width, ghost.Bounds.Width, 1);
            Assert.Equal(size.Height, ghost.Bounds.Height, 1);
            AssertAccent(window, ghost.Background, ghost, .25, .35);
            Assert.Equal(1, control.Opacity);
            Assert.Equal("Map", Part<TextBlock>(control, "PART_CaptionText").Text);
            // The point of the header that was grabbed stays under the pointer, so the preview, and the window a release
            // opens, keep the panel's corner where it was relative to the pointer.
            var moved = at - start;
            Assert.Equal(corner + new PixelVector((int)moved.X, (int)moved.Y), previewWindow.Position);
            Capture(window, $"float-{skin}-{theme}");
            drag.Release();

            Assert.False(previewWindow.IsVisible);
            var root = window.Workspace.FindRoot(window.Workspace.MapTool!, _ => true);
            Assert.NotNull(root?.Window);
            AssertLayoutStillWorks(window, floating: true);
        }
        finally { await Close(window); }
    }

    public static TheoryData<string, string, string, string, string> NewSides()
    {
        var data = new TheoryData<string, string, string, string, string>();
        foreach (var (skin, theme) in new[] { ("System", "Linen"), ("Fleet", "Hull") })
        {
            // Against an edge of the window that has no panel yet: the Workspace and Saved worlds hidden for the left
            // edge, Map and Channels for the right one; top and bottom never have one.
            data.Add(skin, theme, "map", "window", "Left");
            data.Add(skin, theme, "worlds", "window", "Right");
            data.Add(skin, theme, "map", "window", "Top");
            data.Add(skin, theme, "map", "window", "Bottom");
            // Beside the Map, from the column the Map shares (which empties as Channels leaves it) and from the other
            // side of the window (which empties the left column).
            foreach (var side in new[] { "Left", "Right", "Top", "Bottom" }) data.Add(skin, theme, "channels", "map", side);
            data.Add(skin, theme, "worlds", "map", "Left");
            data.Add(skin, theme, "worlds", "map", "Bottom");
        }
        return data;
    }

    /// <summary>
    /// A drop that makes a new column or row gives the panel exactly the width (or height) its preview showed, even
    /// when taking the panel out of its old place widens the panel it splits or the window's empty side.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(NewSides))]
    public async Task ADroppedPanelTakesTheSizeItsPreviewShowed(string skin, string theme, string dragged, string onto, string side)
    {
        var (window, drag) = Open(skin, theme);
        try
        {
            if (onto == "window" && side == "Left") { window.TogglePanel(); window.ToggleSavedWorlds(); }
            if (onto == "window" && side == "Right") { window.ToggleMap(); window.ToggleChannels(); }
            Settle(window);
            drag.Press(drag.Header(dragged));
            TemplatedControl target;
            if (onto == "window")
            {
                drag.Move(drag.Centre(window.GetVisualDescendants().OfType<DockControl>().Single(d => d.Name == "WorkspaceDock")));
                target = window.GetVisualDescendants().OfType<GlobalDockTarget>().Single();
            }
            else
            {
                drag.Move(drag.Centre(drag.Panel(onto)));
                target = drag.LocalTarget();
            }
            drag.Move(drag.Centre(Part<Control>(target, $"PART_{side}Selector")));
            var preview = drag.Bounds(AssertPreview(window, target, $"PART_{side}Indicator"));
            Capture(window, $"size-{dragged}-{onto}-{side}-{skin}-{theme}");
            drag.Release();

            var landed = drag.Bounds(drag.Panel(dragged).FindAncestorOfType<DockableControl>()!);
            // The preview is inset by its 1-DIP margin; the panel loses part of the splitter beside it.
            var horizontal = side is "Left" or "Right";
            var (shown, got) = horizontal ? (preview.Width, landed.Width) : (preview.Height, landed.Height);
            Assert.True(Math.Abs(shown - got) <= 4, $"{dragged} {side} of {onto}: preview {preview}, landed {landed}");
            // Against the window the strip is exactly where the preview was too.
            if (onto == "window") AssertRect(preview, landed, 4);
            AssertLayoutStillWorks(window, hidden: onto == "window" && side is "Left" or "Right");
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task TheGuidesAreDrawnInTheThemeNotDocksBitmaps()
    {
        var (window, drag) = Open("Fleet", "Ember");
        try
        {
            drag.Press(drag.Header("map"));
            drag.Move(drag.Centre(drag.Panel("worlds")));
            foreach (var target in window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DockTargetBase>())
            {
                Assert.Empty(target.GetVisualDescendants().OfType<Image>());
                var guides = target.GetVisualDescendants().OfType<DockGuide>().ToArray();
                Assert.Equal(target is GlobalDockTarget ? 4 : 5, guides.Length);
                var accent = ((ISolidColorBrush)Application.Current!.Resources["AccentBrush"]!).Color;
                Assert.All(guides, g => Assert.Equal(accent, ((ISolidColorBrush)g.Accent!).Color));
            }
            drag.Release();
        }
        finally { await Close(window); }
    }

    // The drop preview is Dock's own indicator, shown at Dock's opacity with the theme's accent at partial alpha.
    private static Control AssertPreview(Window window, TemplatedControl target, string name)
    {
        var indicator = Part<Panel>(target, name);
        Assert.True(indicator.Opacity > 0, $"{name} is shown");
        var fill = indicator.GetVisualDescendants().OfType<Border>().First();
        AssertAccent(window, fill.Background, indicator, .25, .35);
        AssertAccent(window, fill.BorderBrush, indicator, .35, .7);
        Assert.InRange(fill.BorderThickness.Left, 1, 2);
        foreach (var other in new[] { "PART_LeftIndicator", "PART_RightIndicator", "PART_TopIndicator", "PART_BottomIndicator", "PART_CenterIndicator" })
            if (other != name && target.GetVisualDescendants().OfType<Control>().Any(c => c.Name == other && c.TemplatedParent == target))
                Assert.Equal(0, Part<Control>(target, other).Opacity);
        return indicator;
    }

    private static void AssertAccent(Window window, IBrush? brush, Visual shown, double min, double max)
    {
        var accent = ((ISolidColorBrush)Application.Current!.Resources["AccentBrush"]!).Color;
        var color = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        Assert.Equal((accent.R, accent.G, accent.B), (color.R, color.G, color.B));
        var seen = color.A / 255.0 * brush!.Opacity;
        for (Visual? v = shown; v is not null && v != window; v = v.GetVisualParent()) seen *= v.Opacity;
        Assert.InRange(seen, min, max);
    }

    private static void AssertRect(Rect expected, Rect actual, double tolerance)
    {
        Assert.True(Math.Abs(expected.Left - actual.Left) <= tolerance && Math.Abs(expected.Top - actual.Top) <= tolerance
            && Math.Abs(expected.Right - actual.Right) <= tolerance && Math.Abs(expected.Bottom - actual.Bottom) <= tolerance,
            $"expected {expected}, got {actual}");
    }

    private static Rect Half(Rect r, DockOperation side) => side switch
    {
        DockOperation.Left => r.WithWidth(r.Width / 2),
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };

    // A drop leaves a model the View menu still drives: every panel can be hidden and shown again, and Restore panels
    // brings back the default arrangement.
    private static void AssertLayoutStillWorks(MainWindow window, bool floating = false, bool hidden = false)
    {
        Settle(window);
        if (!hidden)
        {
            Assert.True(window.IsMapVisible);
            Assert.True(window.IsChannelsVisible);
            Assert.True(window.IsPanelVisible());
        }
        if (!floating && !hidden)
        {
            window.ToggleMap(); Settle(window);
            Assert.False(window.IsMapVisible);
            window.ToggleMap(); Settle(window);
            Assert.True(window.IsMapVisible);
        }
        window.ResetLayout(); Settle(window);
        Assert.Equal("map-dock", ((IDock)window.Workspace.MapTool!.Owner!).Id);
        Assert.Equal("channels-dock", ((IDock)window.Workspace.ChannelsTool!.Owner!).Id);
    }

    private static T Part<T>(TemplatedControl control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().Single(c => c.Name == name && c.TemplatedParent == control);

    internal static (DragPreviewWindow Window, DragPreviewControl Control) DragPreview()
    {
        var helper = typeof(DockControl).Assembly.GetType("Dock.Avalonia.Internal.DragPreviewHelper")!;
        var window = (DragPreviewWindow)helper.GetField("s_window", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var control = (DragPreviewControl)helper.GetField("s_control", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return (window, control);
    }

    private static (MainWindow, DockDrag) Open(string skin, string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        window.Show(); Settle(window);
        window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = theme });
        Settle(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Settle(window);
        return (window, new DockDrag(window));
    }

    private static async Task Close(MainWindow window)
    {
        if (window.Workspace.FindRoot(window.Workspace.MapTool!, _ => true)?.Window?.Host is Window floating) floating.Close();
        await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new());
    }

    internal static void Settle(Window window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Saves the main window's frame with Dock's drag preview window drawn over it where it sits on screen, which is
    /// what a person sees mid-drag. Only when WANDUR_CAPTURE_DIR names a folder.
    /// </summary>
    internal static void Capture(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Settle(window);
        using var frame = window.CaptureRenderedFrame()!;
        using var main = Decode(frame);
        using var canvas = new SKCanvas(main);
        // The preview window is transparent, which a headless frame does not keep, so its content is rendered on its
        // own with its alpha and laid over the main frame at the window's screen position.
        var helper = typeof(DockControl).Assembly.GetType("Dock.Avalonia.Internal.DragPreviewHelper")!;
        if (helper.GetField("s_window", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) is DragPreviewWindow { IsVisible: true } preview
            && helper.GetField("s_control", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) is DragPreviewControl { Bounds: { Width: > 0, Height: > 0 } bounds } control)
        {
            using var rendered = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height)));
            rendered.Render(control);
            using var stream = new MemoryStream();
            rendered.Save(stream, new PngBitmapEncoderOptions());
            using var ghost = SKBitmap.Decode(stream.ToArray());
            var origin = window.PointToScreen(default);
            canvas.DrawBitmap(ghost, preview.Position.X - origin.X, preview.Position.Y - origin.Y);
        }
        canvas.Flush();
        using var data = main.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), data.ToArray());
    }

    private static SKBitmap Decode(WriteableBitmap frame)
    {
        using var stream = new MemoryStream();
        frame.Save(stream, new PngBitmapEncoderOptions());
        return SKBitmap.Decode(stream.ToArray());
    }

    /// <summary>Drives a drag with headless pointer input, the way a person moves the mouse.</summary>
    internal sealed class DockDrag(MainWindow window)
    {
        private Point _at;

        /// <summary>The content of the tool panel showing <paramref name="toolId"/>.</summary>
        public ToolChromeControl Panel(string toolId) =>
            window.GetVisualDescendants().OfType<ToolChromeControl>()
                .Single(c => c.IsEffectivelyVisible && c.DataContext is IDock { ActiveDockable.Id: var id } && id == toolId);

        /// <summary>A point on the panel's header, clear of its title and buttons.</summary>
        public Point Header(string toolId)
        {
            var header = Panel(toolId).GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip");
            return header.TranslatePoint(new Point(Math.Min(110, header.Bounds.Width / 2), header.Bounds.Height / 2), window)!.Value;
        }

        public Rect Bounds(Visual visual) => new(visual.TranslatePoint(default, window)!.Value, visual.Bounds.Size);
        public Point Centre(Visual visual) => Bounds(visual).Center;

        public DockTarget LocalTarget() => window.GetVisualDescendants().OfType<DockTarget>().Single();

        public void Press(Point at)
        {
            _at = at;
            window.MouseDown(at, MouseButton.Left); Settle(window);
        }

        public void Move(Point to, int steps = 8)
        {
            var from = _at;
            for (var i = 1; i <= steps; i++)
            {
                _at = from + (to - from) * (i / (double)steps);
                window.MouseMove(_at, RawInputModifiers.LeftMouseButton); Settle(window);
            }
        }

        public void Release()
        {
            window.MouseUp(_at, MouseButton.Left); Settle(window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Settle(window);
        }
    }
}
