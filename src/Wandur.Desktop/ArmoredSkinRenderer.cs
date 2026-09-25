using Avalonia;
using Avalonia.Media;

namespace Wandur.Desktop;

/// <summary>Machined plates with fixed-size end details and stretchable straight spans.</summary>
internal static class ArmoredSkinRenderer
{
    private static readonly IBrush Shade = Brush.Parse("#48051117");
    private static readonly IBrush Recess = Brush.Parse("#E00B141A");
    private static readonly IBrush Reflection = Brush.Parse("#65FFFFFF");

    internal static void DrawFrame(DrawingContext context, Size size, Rect title,
        IBrush metal, IBrush edge, IBrush highlight, IBrush accent)
    {
        var w = size.Width; var h = size.Height;
        if (w < 32 || h < 90) return;
        const double band = 64, rail = 12, foot = 18;
        context.DrawRectangle(metal, new Pen(edge, 1), new Rect(.5, .5, w - 1, band - 1), 3, 3);
        var leftEnd = Math.Clamp(title.Left + 8, 6, w - 6);
        var rightStart = Math.Clamp(title.Right - 8, leftEnd, w - 6);
        foreach (var panel in new[] { new Rect(4, 5, Math.Max(0, leftEnd - 4), band - 10),
                     new Rect(rightStart, 5, Math.Max(0, w - rightStart - 4), band - 10) })
        {
            if (panel.Width < 24) continue;
            Plate(context, panel, 4, metal, edge, highlight);
            var y = panel.Bottom - 10;
            context.DrawLine(new Pen(edge, .7), new(panel.Left + 2, y), new(panel.Right - 2, y));
            context.DrawLine(new Pen(highlight, .7), new(panel.Left + 2, y + 1), new(panel.Right - 2, y + 1));
            Fastener(context, new(panel.Left + 7, panel.Bottom - 5), edge, highlight);
            Fastener(context, new(panel.Right - 7, panel.Bottom - 5), edge, highlight);
        }

        foreach (var x in new[] { 0d, w - rail })
        {
            var side = new Rect(x, band, rail, h - band - foot + 4);
            Plate(context, side, 2, metal, edge, highlight);
            // Continuous channel can extend to any height, with fixed top/bottom caps.
            var light = new Rect(x == 0 ? 2 : w - 5, band + 4, 3, Math.Max(0, h - band - foot - 4));
            context.DrawRectangle(Recess, new Pen(edge, .6), light.Inflate(1), 1, 1);
            using (context.PushOpacity(.22)) context.DrawRectangle(accent, null, light.Inflate(2), 2, 2);
            context.DrawRectangle(accent, null, light, 1, 1);
            using (context.PushOpacity(.65)) context.FillRectangle(highlight, new Rect(light.X, light.Y, .7, light.Height));
            var innerLip = x == 0 ? 9 : w - 9;
            context.DrawLine(new Pen(highlight, 1), new(innerLip, band + 4), new(innerLip, h - foot));
        }
        var bottom = new Rect(.5, h - foot, w - 1, foot - .5);
        Plate(context, bottom, 4, metal, edge, highlight);
        var endWidth = Math.Min(100, (w - 24) / 3);
        foreach (var x in new[] { 8d, w - endWidth - 8 })
        {
            var end = new Rect(x, h - foot + 1, endWidth, foot - 3);
            Plate(context, end, 4, metal, edge, highlight);
            var vent = new Rect(x + 16, end.Y + 4, Math.Max(0, endWidth - 32), 7);
            if (vent.Width < 4) continue;
            context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(vent, 2));
            for (var i = 0; i < 9; i++)
            {
                var rib = vent.Left + 4 + i * (vent.Width - 8) / 9;
                context.DrawLine(new Pen(highlight, 1.2), new(rib, vent.Top + 1), new(rib, vent.Bottom - 1));
            }
            Fastener(context, new(end.Left + 7, end.Center.Y), edge, highlight);
            Fastener(context, new(end.Right - 7, end.Center.Y), edge, highlight);
        }
        var latch = new Rect(w / 2 - 32, h - foot + 2, 64, 12);
        Plate(context, latch, 3, metal, edge, highlight);
        for (var i = -1; i <= 1; i++)
            context.DrawRectangle(accent, new Pen(edge, .5), new Rect(w / 2 + i * 7 - 1.5, h - 10, 3, 3));
    }

    internal static void DrawPlaque(DrawingContext context, Rect bounds,
        IBrush metal, IBrush inset, IBrush edge, IBrush accent)
    {
        var w = bounds.Width; var h = bounds.Height;
        if (w < 208 || h < 40) return;
        var outline = Polygon([new(.5, 21), new(10, 21), new(23, 2), new(45, 2), new(50, 5),
            new(w - 50, 5), new(w - 45, 2), new(w - 23, 2), new(w - 10, 21), new(w - .5, 21),
            new(w - .5, h - 10), new(w - 10, h - 2), new(10, h - 2), new(.5, h - 10)]);
        using (context.PushTransform(Matrix.CreateTranslation(0, 2)))
            context.DrawGeometry(Shade, new Pen(Shade, 4), outline);
        context.DrawGeometry(metal, new Pen(edge, 1), outline);
        ArmoredWear.Draw(context, outline, bounds);
        context.DrawLine(new Pen(Reflection, 1), new(51, 6), new(w - 51, 6));
        context.DrawLine(new Pen(Reflection, 1), new(10, h - 3), new(w - 10, h - 3));
        // Layered dark receiver; shoulders stand proud of the center display.
        var receiver = new Rect(49, 7, w - 98, h - 14);
        context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(receiver, 6));
        context.DrawGeometry(null, new Pen(Reflection, 1), Chamfer(receiver.Deflate(2), 5));
        var well = receiver.Deflate(6);
        context.DrawGeometry(inset, new Pen(edge, 1), Chamfer(well, 4));
        context.DrawLine(new Pen(Shade, 3), new(well.Left + 5, well.Top + 2), new(well.Right - 5, well.Top + 2));
        context.DrawLine(new Pen(Reflection, .8), new(well.Left + 5, well.Bottom - 1), new(well.Right - 5, well.Bottom - 1));
        foreach (var left in new[] { true, false })
        {
            var x = left ? 13d : w - 47;
            var collar = new Rect(x, 7, 34, h - 14);
            Plate(context, collar, 4, metal, edge, Reflection);
            var socket = new Rect(left ? x + 15 : x + 5, 16, 14, h - 32);
            context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(socket, 3));
            context.DrawLine(new Pen(Reflection, .8), new(socket.Left + 1, socket.Bottom - 3), new(socket.Left + 1, socket.Top + 3));
            var light = new Rect(socket.Center.X - 1.5, socket.Top + 5, 3, socket.Height - 10);
            using (context.PushOpacity(.22)) context.DrawRectangle(accent, null, light.Inflate(2), 2, 2);
            context.DrawRectangle(accent, null, light, 1.5, 1.5);
            context.DrawLine(new Pen(Reflection, .8), new(light.Left + .7, light.Top + 1), new(light.Left + .7, light.Bottom - 1));
            foreach (var y in new[] { collar.Top + 5, collar.Bottom - 5 })
                Fastener(context, new(left ? collar.Left + 6 : collar.Right - 6, y), edge, Reflection);
        }
        foreach (var x in new[] { receiver.Left + 9, receiver.Right - 9 })
        foreach (var y in new[] { receiver.Top + 4, receiver.Bottom - 4 })
            Fastener(context, new(x, y), edge, Reflection);
    }

    internal static IBrush CreateToolbar(Size size, Rect title, IBrush metal, IBrush edge, IBrush highlight)
    {
        if (size.Width < 2 || size.Height < 2) return metal;
        var left = Math.Clamp(title.Left - 5, 0, size.Width);
        var right = Math.Clamp(title.Right + 5, left, size.Width);
        var bottom = Math.Clamp(title.Bottom + 4, 3, Math.Max(3, size.Height - 1));
        var cap = Math.Min(6, (right - left) / 2);
        Point[] rim = title.Width >= 208 && title.Bottom > 0
            ? [new(0, 2), new(left, 2), new(left, bottom - cap), new(left + cap, bottom),
               new(right - cap, bottom), new(right, bottom - cap), new(right, 2), new(size.Width, 2)]
            : [new(0, 2), new(size.Width, 2)];
        var face = Polygon([.. rim, new(size.Width, size.Height), new(0, size.Height)]);
        var seam = Polygon(rim, false);
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(size)) };
        using (var context = drawing.Open())
        {
            context.FillRectangle(edge, new Rect(size));
            context.DrawGeometry(metal, null, face);
            context.DrawGeometry(null, new Pen(Shade, 7), seam);
            context.DrawGeometry(null, new Pen(edge, 1), seam);
            using (context.PushTransform(Matrix.CreateTranslation(0, 2)))
                context.DrawGeometry(null, new Pen(highlight, 1), seam);
            context.DrawLine(new Pen(edge, 1), new(0, size.Height - 1), new(size.Width, size.Height - 1));
        }
        return new DrawingBrush { Drawing = drawing, Stretch = Stretch.Fill,
            SourceRect = new RelativeRect(new Rect(size), RelativeUnit.Absolute) };
    }

    private static void Plate(DrawingContext context, Rect bounds, double cap, IBrush metal, IBrush edge, IBrush highlight)
    {
        var shape = Chamfer(bounds, cap);
        context.DrawGeometry(metal, new Pen(edge, 1), shape);
        ArmoredWear.Draw(context, shape, bounds);
        if (bounds.Width < 5 || bounds.Height < 5) return;
        context.DrawGeometry(null, new Pen(highlight, .7), Chamfer(bounds.Deflate(1.2), Math.Max(1, cap - 1)));
        context.DrawLine(new Pen(Shade, 1.5), new(bounds.Left + cap, bounds.Bottom - 1), new(bounds.Right - cap, bounds.Bottom - 1));
    }

    private static void Fastener(DrawingContext context, Point point, IBrush edge, IBrush highlight)
    {
        context.DrawEllipse(edge, null, point, 1.3, 1.3);
        context.DrawLine(new Pen(highlight, .6), point + new Vector(-.7, .6), point + new Vector(.7, .6));
    }

    private static StreamGeometry Chamfer(Rect r, double cap) => Polygon([
        new(r.Left + cap, r.Top), new(r.Right - cap, r.Top), new(r.Right, r.Top + cap),
        new(r.Right, r.Bottom - cap), new(r.Right - cap, r.Bottom), new(r.Left + cap, r.Bottom),
        new(r.Left, r.Bottom - cap), new(r.Left, r.Top + cap)]);

    private static StreamGeometry Polygon(Point[] points, bool closed = true)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(points[0], closed);
        for (var i = 1; i < points.Length; i++) path.LineTo(points[i]);
        path.EndFigure(closed);
        return geometry;
    }
}
