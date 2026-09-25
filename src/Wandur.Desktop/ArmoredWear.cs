using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Wandur.Desktop;

/// <summary>App-lifetime alpha mask, tiled at fixed DIP scale and lit in two neutral passes.</summary>
internal static class ArmoredWear
{
    private static readonly Lazy<ImageBrush> Mask = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://Wandur/Assets/Skins/armored-wear.png"));
        return new ImageBrush(new Bitmap(stream))
        {
            TileMode = TileMode.Tile, Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
            DestinationRect = new RelativeRect(0, 0, 384, 384, RelativeUnit.Absolute)
        };
    });

    internal static void Draw(DrawingContext context, Geometry clip, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using (context.PushGeometryClip(clip))
        {
            // Ignore RGB: the same scratches acquire depth on any palette.
            using (context.PushOpacity(.28))
            using (context.PushTransform(Matrix.CreateTranslation(.5, .75)))
            using (context.PushOpacityMask(Mask.Value, bounds))
                context.FillRectangle(Brushes.White, bounds);
            using (context.PushOpacity(.32))
            using (context.PushOpacityMask(Mask.Value, bounds))
                context.FillRectangle(Brushes.Black, bounds);
        }
    }
}
