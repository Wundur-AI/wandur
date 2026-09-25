using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace Wandur.Desktop.Tests;

public sealed class SkinRefinementTests
{
    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(1536)]
    public async Task SystemAppearanceButtonsStayInToolbarAndOpenTheirMenus(int width)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Width = width; window.Show();
            foreach (var previous in new[] { "Fleet", "Armored", "System" })
            {
                window.Sessions.PreviewAppearanceSettings(new() { Skin = previous });
                window.Sessions.PreviewAppearanceSettings(new() { Skin = "System" });
                WindowSkinTransitionTests.Settle(window);
                var toolbar = WindowSkinTransitionTests.Named<Border>(window, "MainToolbar");
                var title = WindowSkinTransitionTests.Named<TextBlock>(window, "AppTitle");
                foreach (var name in new[] { "TitleSkinButton", "TitleThemeButton" })
                {
                    var button = WindowSkinTransitionTests.Named<Button>(window, name);
                    Assert.True(button.IsEffectivelyVisible, $"{name} must remain available in System.");
                    Assert.Contains(toolbar, button.GetVisualAncestors());
                    var position = button.TranslatePoint(default, window)!.Value;
                    Assert.True(position.X >= title.TranslatePoint(default, window)!.Value.X + title.Bounds.Width);
                    Assert.True(position.X + button.Bounds.Width <= window.ClientSize.Width - toolbar.Padding.Right);
                    Assert.InRange(position.Y, 0, 48 - button.Bounds.Height);
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                    WindowSkinTransitionTests.Settle(window);
                    var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
                    Assert.True(flyout.IsOpen, "Appearance buttons must receive clicks, not titlebar drags.");
                    flyout.Hide();
                }
                window.ToggleFullScreen(); WindowSkinTransitionTests.Settle(window);
                window.ToggleFullScreen(); WindowSkinTransitionTests.Settle(window);
                Assert.True(WindowSkinTransitionTests.Named<Button>(window, "TitleSkinButton").IsEffectivelyVisible);
            }
            Capture(window, $"refined-System-{width}");
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public void WearTileIsSparseTransparentAndHasNoCutOffEdges()
    {
        var uri = new Uri("avares://Wandur/Assets/Skins/armored-wear.png");
        Assert.True(AssetLoader.Exists(uri), "Armored needs its packaged transparent wear tile.");
        using var stream = AssetLoader.Open(uri);
        using var bitmap = SkiaSharp.SKBitmap.Decode(stream);
        var painted = 0;
        foreach (var pixel in bitmap.Pixels) if (pixel.Alpha > 0) painted++;
        Assert.InRange((double)painted / (bitmap.Width * bitmap.Height), .0001, .10);
        for (var i = 0; i < bitmap.Width; i++)
        {
            Assert.Equal(0, bitmap.GetPixel(i, 0).Alpha);
            Assert.Equal(0, bitmap.GetPixel(i, bitmap.Height - 1).Alpha);
        }
        for (var i = 0; i < bitmap.Height; i++)
        {
            Assert.Equal(0, bitmap.GetPixel(0, i).Alpha);
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, i).Alpha);
        }
    }

    private sealed class WearSurface : Control
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brush.Parse("#C46230"), new Rect(Bounds.Size));
            var area = new Rect(0, 8, Bounds.Width, Bounds.Height - 16);
            ArmoredWear.Draw(context, new RectangleGeometry(area), new Rect(Bounds.Size));
        }
    }

    [AvaloniaFact]
    public void WearRepeatsWithoutStretchingAndOnlyChangesItsClippedSurface()
    {
        var window = new Window { Width = 768, Height = 384, Content = new WearSurface() };
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            using var before = Pixels(window);
            var baseColor = before.GetPixel(0, 0);
            var changed = 0;
            for (var y = 0; y < 384; y++)
            for (var x = 0; x < 384; x++)
            {
                var color = before.GetPixel(x, y);
                Assert.Equal(color, before.GetPixel(x + 384, y));
                if (y < 8 || y >= 376) Assert.Equal(baseColor, color);
                if (color != baseColor) changed++;
            }
            Assert.InRange(changed, 10, 384 * 384 / 10);
            window.Width = 1152; WindowSkinTransitionTests.Settle(window);
            using var after = Pixels(window);
            for (var y = 0; y < 384; y += 2)
            for (var x = 0; x < 768; x += 2)
                Assert.Equal(before.GetPixel(x, y), after.GetPixel(x, y));
        }
        finally { window.Close(); }
    }

    private static SkiaSharp.SKBitmap Pixels(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        using var stream = new MemoryStream(); frame.Save(stream, new PngBitmapEncoderOptions());
        return SkiaSharp.SKBitmap.Decode(stream.ToArray());
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaTheory]
    [InlineData("Hull")]
    [InlineData("Slate")]
    [InlineData("Paper")]
    public async Task ArmoredFootAndShoulderDetailsStayOutsideLiveContent(string palette)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); await window.Controller.StartAsync();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = "Armored", Theme = palette });
            foreach (var width in new[] { 1040, 1536 })
            foreach (var scale in new[] { 1d, 2d })
            {
                window.Width = width; window.Height = 900; window.SetRenderScaling(scale);
                WindowSkinTransitionTests.Settle(window);
                var host = Assert.Single(window.GetVisualDescendants().OfType<ThemeWindowSkinHost>());
                // The vented feet occupy 18 DIP, never draw over status/content.
                Assert.True(host.Bounds.Height - host.Child!.Bounds.Bottom >= 18);
                Assert.True(host.Child.Bounds.Left >= 12);
                var failures = ContrastProbe.Scan(window);
                Assert.Empty(failures);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    frame.Save(Path.Combine(directory, $"refined-Armored-{palette}-{width}-{scale}x.png"), new PngBitmapEncoderOptions());
                }
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }
}
