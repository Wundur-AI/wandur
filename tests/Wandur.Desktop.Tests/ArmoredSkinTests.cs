using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Wandur.Desktop.Tests;

public sealed class ArmoredSkinTests
{
    [Theory]
    [InlineData(800, 100, 160, 4000)]
    [InlineData(1536, 88, 92, 120)]
    [InlineData(320, 0, 0, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(double.NaN, double.PositiveInfinity, -1, double.NaN)]
    public void LayoutIsFiniteCenteredAndKeepsFixedCaps(double width, double left, double right, double text)
    {
        var place = ArmoredTitleLayout.Calculate(width, left, right, text);
        Assert.True(double.IsFinite(place.Bounds.Width));
        Assert.True(place.Bounds.Width >= 0);
        var available = double.IsFinite(width) ? width : 0;
        Assert.Equal(available / 2, place.Bounds.Center.X, 5);
        Assert.InRange(place.Bounds.Right, 0, available);
        if (width == 800)
        {
            Assert.True(place.Bounds.Left >= 100);
            Assert.True(place.Bounds.Right <= 640);
        }
        if (width == 1536) Assert.Equal(280, place.Bounds.Width);
        if (available == 0) Assert.True(place.PlainTitle);
    }

    [AvaloniaTheory]
    [InlineData(1040, "Hull")]
    [InlineData(1536, "Hull")]
    [InlineData(1040, "Slate")]
    public async Task ArmoredPlateIsRecessedBelowItsBandWithContinuousSideLights(int width, string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Width = width; window.Height = 900; window.Show();
            window.Controller.SaveSettings(window.Controller.Settings with { Theme = theme, Skin = "Armored", UseWorldThemes = false });
            await window.Controller.StartAsync();
            WindowSkinTransitionTests.Settle(window);
            var plaque = WindowSkinTransitionTests.Named<Border>(window, "PlaqueTitleHost");
            var toolbar = WindowSkinTransitionTests.Named<Border>(window, "MainToolbar");
            var bottom = plaque.TranslatePoint(new Point(0, plaque.Bounds.Height), window)!.Value.Y;
            var top = toolbar.TranslatePoint(default, window)!.Value.Y;
            Assert.InRange(bottom - top, 6, 12);
            var shape = window.GetVisualDescendants().OfType<ThemePlaque>().Single();
            Assert.Equal("armored", shape.Shape);
            Assert.False(shape.IsHitTestVisible);
            Assert.Null(window.GetVisualDescendants().OfType<ThemeWindowSkinHost>().Single().BorderBitmap);
            var actions = WindowSkinTransitionTests.Named<StackPanel>(window, "TitleActions");
            Assert.True(plaque.TranslatePoint(new Point(plaque.Bounds.Width, 0), window)!.Value.X <= actions.TranslatePoint(default, window)!.Value.X);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            using var stream = new MemoryStream(); frame.Save(stream, new PngBitmapEncoderOptions());
            using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
            var host = window.GetVisualDescendants().OfType<ThemeWindowSkinHost>().Single();
            var origin = host.TranslatePoint(default, window)!.Value;
            foreach (var y in new[] { 220, 420, 620 })
            foreach (var x in new[] { origin.X + 3, origin.X + host.Bounds.Width - 4 })
            {
                var pixel = pixels.GetPixel((int)x, y);
                Assert.True(pixel.Green > pixel.Red && pixel.Blue > pixel.Red, $"Expected continuous cool accent at {x},{y}: {pixel}");
            }
            if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"armored-{theme}-{width}.png"), new PngBitmapEncoderOptions());
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }
}
