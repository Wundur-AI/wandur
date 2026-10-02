using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Wandur.Desktop.Tests;

/// <summary>
/// Frames for the help pages: a whole window, or a control cropped with a margin. Open popups (menus, flyouts) are
/// their own top levels in the headless platform, so they are drawn over the window's frame where they really sit.
/// Pictures are kept only when WANDUR_CAPTURE_DIR is set; every frame is still rendered and checked.
/// </summary>
internal static class HelpCapture
{
    public const int Margin = 16;

    public static string? Directory => Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory ? directory : null;

    public static void Settle(TopLevel top, int ticks = 4)
    {
        Dispatcher.UIThread.RunJobs();
        top.UpdateLayout();
        foreach (var popup in Popups(top)) popup.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(ticks);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The whole window, with any open popup over it.</summary>
    public static PixelSize Window(TopLevel top, string name, params Visual[] popups)
    {
        using var frame = Frame(top, popups);
        return Save(frame, new PixelRect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height), name);
    }

    /// <summary>The union of the controls' bounds plus the margin, clamped to the frame. Popups are included.</summary>
    public static PixelSize Crop(TopLevel top, string name, params Visual[] controls) => Crop(top, name, Margin, controls);

    public static PixelSize Crop(TopLevel top, string name, int margin, params Visual[] controls)
    {
        Assert.NotEmpty(controls);
        var bounds = controls.Select(control => BoundsIn(control, top)).Aggregate((a, b) => a.Union(b));
        return CropRect(top, name, bounds.Inflate(margin), controls);
    }

    /// <summary>A rectangle of the window in its own units, clamped to the frame. Controls named here that live in a popup
    /// bring that popup into the frame.</summary>
    public static PixelSize CropRect(TopLevel top, string name, Rect rect, params Visual[] popups)
    {
        using var frame = Frame(top, popups);
        var scale = top.RenderScaling;
        var left = Math.Clamp((int)Math.Floor(rect.X * scale), 0, frame.PixelSize.Width);
        var topEdge = Math.Clamp((int)Math.Floor(rect.Y * scale), 0, frame.PixelSize.Height);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right * scale), 0, frame.PixelSize.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom * scale), 0, frame.PixelSize.Height);
        Assert.True(right - left > 40 && bottom - topEdge > 20, $"The crop for {name} is empty: {rect}.");
        return Save(frame, new PixelRect(left, topEdge, right - left, bottom - topEdge), name);
    }

    /// <summary>Where a control sits in the top level, whether it lives in the window or in one of its popups.</summary>
    public static Rect BoundsIn(Visual control, TopLevel top)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.GetType().Name} {(control as Control)?.Name} is not visible.");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{control.GetType().Name} {(control as Control)?.Name} has no size.");
        var owner = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("The control is not shown.");
        var origin = control.TranslatePoint(default, owner) ?? throw new InvalidOperationException("The control has no position.");
        if (!ReferenceEquals(owner, top)) origin += Offset(owner, top);
        return new Rect(origin, control.Bounds.Size);
    }

    private static Point Offset(TopLevel popup, TopLevel top)
    {
        var screen = popup.PointToScreen(default);
        var window = top.PointToScreen(default);
        return new Point((screen.X - window.X) / top.RenderScaling, (screen.Y - window.Y) / top.RenderScaling);
    }

    /// <summary>Open popups found in the window's tree (menus), and the popups of those (submenus).</summary>
    private static IEnumerable<TopLevel> Popups(TopLevel top)
    {
        var found = new List<TopLevel>();
        void Walk(Visual root)
        {
            foreach (var popup in root.GetVisualDescendants().OfType<Popup>().Where(popup => popup.IsOpen && popup.Child is not null))
                if (TopLevel.GetTopLevel(popup.Child) is { } host && !ReferenceEquals(host, top) && !found.Contains(host))
                { found.Add(host); Walk(host); }
        }
        Walk(top);
        return found;
    }

    private static WriteableBitmap Frame(TopLevel top, IEnumerable<Visual> popups)
    {
        Settle(top);
        using var window = top.CaptureRenderedFrame();
        Assert.NotNull(window);
        var size = window!.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { window.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { handle.Free(); }
        var format = window.Format ?? PixelFormat.Bgra8888;
        var layers = Popups(top).Concat(popups.Select(visual => visual is Popup { Child: { } child } ? child : visual).Select(TopLevel.GetTopLevel).OfType<TopLevel>()).Where(layer => !ReferenceEquals(layer, top)).Distinct().ToArray();
        foreach (var popup in layers)
        {
            using var layer = popup.CaptureRenderedFrame();
            if (layer is null) continue;
            var at = Offset(popup, top);
            Blend(pixels, size, layer, (int)Math.Round(at.X * top.RenderScaling), (int)Math.Round(at.Y * top.RenderScaling));
        }
        var result = new WriteableBitmap(size, new Vector(96, 96), format, AlphaFormat.Premul);
        using (var buffer = result.Lock()) Marshal.Copy(pixels, 0, buffer.Address, Math.Min(pixels.Length, buffer.RowBytes * size.Height));
        return result;
    }

    /// <summary>Premultiplied source-over of a popup's frame onto the window's pixels.</summary>
    private static void Blend(byte[] target, PixelSize size, Bitmap layer, int x, int y)
    {
        var layerSize = layer.PixelSize;
        var stride = layerSize.Width * 4;
        var source = new byte[stride * layerSize.Height];
        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try { layer.CopyPixels(new PixelRect(layerSize), handle.AddrOfPinnedObject(), source.Length, stride); }
        finally { handle.Free(); }
        for (var row = 0; row < layerSize.Height; row++)
        {
            var ty = y + row;
            if (ty < 0 || ty >= size.Height) continue;
            for (var column = 0; column < layerSize.Width; column++)
            {
                var tx = x + column;
                if (tx < 0 || tx >= size.Width) continue;
                var s = row * stride + column * 4;
                var t = (ty * size.Width + tx) * 4;
                var alpha = source[s + 3];
                if (alpha == 0) continue;
                for (var channel = 0; channel < 4; channel++)
                    target[t + channel] = (byte)Math.Min(255, source[s + channel] + target[t + channel] * (255 - alpha) / 255);
            }
        }
    }

    private static PixelSize Save(WriteableBitmap frame, PixelRect rect, string name)
    {
        if (Directory is not { } directory) return rect.Size;
        System.IO.Directory.CreateDirectory(directory);
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(rect, handle.AddrOfPinnedObject(), pixels.Length, stride);
            using var cropped = new Bitmap(frame.Format ?? PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), rect.Size, new Vector(96, 96), stride);
            cropped.Save(Path.Combine(directory, name), new PngBitmapEncoderOptions());
        }
        finally { handle.Free(); }
        return rect.Size;
    }
}
