using Avalonia.Headless.XUnit;
using SkiaSharp;
using Wandur.Core.Discovery;
using Wandur.Desktop.Services;

namespace Wandur.Desktop.Tests;

/// <summary>Thumbnails decode near their size; the result keeps the old sizes and colors whatever the source format.</summary>
public sealed class WorldThumbnailDecodeTests
{
    private static byte[] Picture(int width, int height, SKEncodedImageFormat format)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(new SKColor(200, 40, 40));
        using var paint = new SKPaint { Color = new SKColor(20, 40, 220) };
        // Left half blue, right half red: a scaled picture must keep the halves where they were.
        surface.Canvas.DrawRect(0, 0, width / 2f, height, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static WorldThumbnails Thumbnails(int width, int height, bool cover) =>
        new(new WorldCatalog(Path.Combine(Path.GetTempPath(), "wandur-thumb-" + Guid.NewGuid(), "directory.json"), new Uri("http://127.0.0.1:9/"), OfflineHttp.Client()),
            width, height, cacheBitmaps: false, cover: cover);

    [AvaloniaTheory]
    [InlineData(SKEncodedImageFormat.Jpeg, 4000, 3000, 800, 320, true, 800, 600)]
    [InlineData(SKEncodedImageFormat.Png, 4000, 3000, 800, 320, true, 800, 600)]
    [InlineData(SKEncodedImageFormat.Jpeg, 4000, 3000, 80, 60, false, 80, 60)]
    [InlineData(SKEncodedImageFormat.Jpeg, 3001, 1999, 80, 60, false, 80, 53)]
    [InlineData(SKEncodedImageFormat.Webp, 2400, 1000, 800, 320, true, 800, 333)]
    [InlineData(SKEncodedImageFormat.Png, 300, 120, 800, 320, true, 300, 120)]
    public void LargeArtworkShrinksToTheSameSizeAsAFullDecodeWould(SKEncodedImageFormat format, int width, int height, int boxWidth, int boxHeight, bool cover, int expectedWidth, int expectedHeight)
    {
        using var thumbnails = Thumbnails(boxWidth, boxHeight, cover);
        using var bitmap = thumbnails.Shrink(Picture(width, height, format));
        Assert.NotNull(bitmap);
        Assert.Equal(new Avalonia.PixelSize(expectedWidth, expectedHeight), bitmap.PixelSize);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var decoded = SKBitmap.Decode(stream.ToArray());
        var left = decoded.GetPixel(decoded.Width / 4, decoded.Height / 2);
        var right = decoded.GetPixel(decoded.Width * 3 / 4, decoded.Height / 2);
        Assert.True(left.Blue > 180 && left.Red < 60, $"left {left}");
        Assert.True(right.Red > 160 && right.Blue < 80, $"right {right}");
    }

    [AvaloniaFact]
    public void BytesThatAreNotAPictureGiveNoThumbnail()
    {
        using var thumbnails = Thumbnails(80, 60, false);
        Assert.Null(thumbnails.Shrink("<html>not art</html>"u8.ToArray()));
    }
}
