using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Wandur.Desktop.Tests;

public sealed class ArmoredConstructionTests
{
    private sealed class FrameSurface(double width) : Control
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.Magenta, new Rect(Bounds.Size));
            var metrics = TitleBarMetrics.Armored;
            ArmoredSkinRenderer.DrawFrame(context, new Size(width, 700), metrics.BandHeight,
                new Rect(width / 2 - 230, metrics.PlaqueTop, 460, metrics.PlaqueHeight), new Thickness(88, 0, 276, 0), Brushes.LightGray, Brushes.DimGray, Brushes.White, Brushes.Cyan);
        }
    }

    [AvaloniaTheory]
    [InlineData(1040)]
    [InlineData(1536)]
    public void BroadHeaderBevelIsLitSeparatelyFromItsFaceWithoutEnteringContent(double width)
    {
        var window = new Window { Width = width, Height = 700, Content = new FrameSurface(width) };
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            using var pixels = Pixels(window);
            // Broad top-facing metal must catch light, not merely have a one-pixel outline.
            foreach (var x in new[] { 60, (int)width - 60 })
                Assert.True(pixels.GetPixel(x, 8).Red > pixels.GetPixel(x, 32).Red + 15);
            // The corner casting must not interrupt either continuous cyan channel.
            foreach (var x in new[] { 3, (int)width - 4 })
            for (var y = 630; y <= 664; y += 2)
            {
                var light = pixels.GetPixel(x, y);
                Assert.True(light.Green > light.Red + 50 && light.Blue > light.Red + 50,
                    $"Corner interrupts light at ({x}, {y}).");
            }
            // Corner shoulders must stay in the existing 24/30-DIP frame footprint.
            for (var y = (int)TitleBarMetrics.Armored.BandHeight + 1; y < 670; y += 7)
            for (var x = 25; x < width - 24; x += 7)
                Assert.True(SKColors.Magenta == pixels.GetPixel(x, y), $"Frame enters content at ({x}, {y}).");
        }
        finally { window.Close(); }
    }

    private sealed class ConstructionViewport(double x, double y) : Control
    {
        public override void Render(DrawingContext context)
        {
            using var offset = context.PushTransform(Matrix.CreateTranslation(-x, -y));
            var metrics = TitleBarMetrics.Armored;
            var title = new Rect(538, metrics.PlaqueTop, 460, metrics.PlaqueHeight);
            ArmoredSkinRenderer.DrawFrame(context, new Size(1536, 900), metrics.BandHeight, title, new Thickness(88, 0, 276, 0),
                FleetSkin.Metal, FleetSkin.RimEdge, FleetSkin.RimHighlight, Brush.Parse("#8DDEE5"));
            using var titleOffset = context.PushTransform(Matrix.CreateTranslation(title.X, title.Y));
            ArmoredSkinRenderer.DrawPlaque(context, new Rect(title.Size), FleetSkin.Metal,
                FleetSkin.Plaque, FleetSkin.RimEdge, Brush.Parse("#8DDEE5"));
        }
    }

    [AvaloniaTheory]
    [InlineData("header", 448, 0, 640, 110)]
    [InlineData("corner", 0, 800, 180, 100)]
    public void ConstructionCloseupsRenderAtDoubleScale(string name, double x, double y, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Content = new ConstructionViewport(x, y) };
        try
        {
            ThemeService.Apply(new() { Skin = "Armored", Theme = "Hull" });
            window.Show(); window.SetRenderScaling(2); WindowSkinTransitionTests.Settle(window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            Assert.Equal(width * 2, frame.PixelSize.Width);
            Assert.Equal(height * 2, frame.PixelSize.Height);
            if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"construction-{name}-2x.png"), new PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); ThemeService.Apply(new()); }
    }

    private static SKBitmap Pixels(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        using var stream = new MemoryStream(); frame.Save(stream, new PngBitmapEncoderOptions());
        return SKBitmap.Decode(stream.ToArray());
    }
}
