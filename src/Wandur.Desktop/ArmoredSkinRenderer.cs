using Avalonia;
using Avalonia.Media;

namespace Wandur.Desktop;

/// <summary>Machined plates with fixed-size end details and stretchable straight spans.</summary>
internal static class ArmoredSkinRenderer
{
    private static readonly IBrush Shade = Brush.Parse("#48051117");
    private static readonly IBrush Recess = Brush.Parse("#E00B141A");
    private static readonly IBrush Reflection = Brush.Parse("#65FFFFFF");

    internal static void DrawFrame(DrawingContext context, Size size, Rect title, Thickness captionExclusion,
        IBrush metal, IBrush edge, IBrush highlight, IBrush accent)
    {
        var w = size.Width; var h = size.Height;
        const double band = WindowSkinDefinition.ArmoredBandHeight;
        const double rail = WindowSkinDefinition.ArmoredRailWidth;
        const double foot = WindowSkinDefinition.ArmoredFootHeight;
        if (w < 32 || h < band + foot + 54) return;
        context.DrawRectangle(metal, new Pen(edge, 1), new Rect(.5, .5, w - 1, band - 1), 3, 3);
        var leftEnd = Math.Clamp(title.Left + 8, 6, w - 6);
        var rightStart = Math.Clamp(title.Right - 8, leftEnd, w - 6);
        foreach (var panel in new[] { new Rect(3, 4, Math.Max(0, leftEnd - 3), band - 8),
                     new Rect(rightStart, 4, Math.Max(0, w - rightStart - 3), band - 8) })
        {
            if (panel.Width < 24) continue;
            Plate(context, panel, 7, metal, edge, highlight);
            var y = panel.Bottom - 14;
            context.DrawLine(new Pen(Shade, 3), new(panel.Left + 4, y), new(panel.Right - 4, y));
            context.DrawLine(new Pen(highlight, 1), new(panel.Left + 4, y + 2), new(panel.Right - 4, y + 2));
            // Short lower service plates sit below the caption controls, not behind them.
            var serviceWidth = Math.Min(110, panel.Width * .38);
            foreach (var sx in new[] { panel.Left + 10, panel.Right - serviceWidth - 10 })
                Plate(context, new Rect(sx, y + 3, serviceWidth, 9), 2, metal, edge, highlight);
            if (panel.Width > 240)
            {
                // A service hatch beside each central shoulder, clear of native/action buttons.
                var hatchX = panel.Left < w / 2 ? panel.Right - 139 : panel.Left + 8;
                var hatch = new Rect(hatchX, panel.Top + 11, 130, 44);
                if (hatch.Left >= captionExclusion.Left + 4 && hatch.Right <= w - captionExclusion.Right - 4)
                {
                    context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(hatch, 4));
                    Plate(context, hatch.Deflate(2), 3, metal, edge, highlight);
                    foreach (var hx in new[] { hatch.Left + 7, hatch.Right - 7 })
                        Fastener(context, new(hx, hatch.Bottom - 6), edge, highlight);
                    var hinge = new Rect(hatch.Center.X - 11, hatch.Top - 1, 22, 4);
                    context.DrawRectangle(metal, new Pen(edge, .7), hinge, 1, 1);
                    context.DrawLine(new Pen(edge, 1), new(hatch.Center.X - 13, hatch.Center.Y), new(hatch.Center.X + 13, hatch.Center.Y));
                    context.DrawLine(new Pen(highlight, 1), new(hatch.Center.X - 13, hatch.Center.Y + 1), new(hatch.Center.X + 13, hatch.Center.Y + 1));
                }
            }
            Fastener(context, new(panel.Left + 7, panel.Bottom - 5), edge, highlight);
            Fastener(context, new(panel.Right - 7, panel.Bottom - 5), edge, highlight);
        }

        foreach (var x in new[] { 0d, w - rail })
        {
            var side = new Rect(x, band - 3, rail, h - band - foot + 6);
            context.FillRectangle(Recess, side);
            Plate(context, new Rect(x == 0 ? 1 : w - rail + 6, side.Top, rail - 7, side.Height), 4, metal, edge, highlight);
            // Continuous channel can extend to any height, with fixed top/bottom caps.
            var light = new Rect(x == 0 ? 2 : w - 5, band + 4, 3, Math.Max(0, h - band - foot - 4));
            context.DrawRectangle(Recess, new Pen(edge, .6), light.Inflate(1), 1, 1);
            using (context.PushOpacity(.22)) context.DrawRectangle(accent, null, light.Inflate(2), 2, 2);
            context.DrawRectangle(accent, null, light, 1, 1);
            using (context.PushOpacity(.65)) context.FillRectangle(highlight, new Rect(light.X, light.Y, .7, light.Height));
            var innerLip = x == 0 ? rail - 3 : w - rail + 3;
            context.DrawLine(new Pen(edge, 4), new(innerLip, band), new(innerLip, h - foot));
            context.DrawLine(new Pen(highlight, 1), new(innerLip + (x == 0 ? -2 : 2), band),
                new(innerLip + (x == 0 ? -2 : 2), h - foot));
            // Fixed-size inspection joints. Only the straight rail and light channel extend.
            foreach (var fraction in new[] { .0, .34, .70, 1.0 })
            {
                var y = band + 9 + fraction * Math.Max(0, h - band - foot - 54);
                var px = x == 0 ? 7 : w - 21;
                Plate(context, new Rect(px, y, 14, 42), 2, metal, edge, highlight);
                Fastener(context, new(px + 7, y + 6), edge, highlight);
                Fastener(context, new(px + 7, y + 36), edge, highlight);
                context.DrawLine(new Pen(Shade, 2), new(px + 5, y + 15), new(px + 5, y + 27));
                context.DrawLine(new Pen(highlight, .8), new(px + 7, y + 15), new(px + 7, y + 27));
            }
        }
        var bottom = new Rect(.5, h - foot, w - 1, foot - .5);
        context.FillRectangle(Recess, bottom);
        Plate(context, bottom.Deflate(new Thickness(1, 3)), 6, metal, edge, highlight);
        context.DrawLine(new Pen(Shade, 3), new(rail, bottom.Top + 1), new(w - rail, bottom.Top + 1));
        context.DrawLine(new Pen(highlight, 1), new(rail, bottom.Top + 3), new(w - rail, bottom.Top + 3));
        var endWidth = Math.Min(138, (w - 24) / 3);
        foreach (var x in new[] { 5d, w - endWidth - 5 })
        {
            var end = new Rect(x, h - foot + 3, endWidth, foot - 6);
            Plate(context, end, 7, metal, edge, highlight);
            var vent = new Rect(x + 18, end.Y + 5, Math.Max(0, endWidth - 36), 14);
            if (vent.Width < 4) continue;
            context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(vent, 2));
            for (var i = 0; i < 9; i++)
            {
                var rib = vent.Left + 4 + i * (vent.Width - 8) / 9;
                context.DrawLine(new Pen(edge, 3), new(rib, vent.Top + 2), new(rib, vent.Bottom - 2));
                context.DrawLine(new Pen(highlight, .8), new(rib - 1, vent.Top + 2), new(rib - 1, vent.Bottom - 2));
            }
            Fastener(context, new(end.Left + 7, end.Center.Y), edge, highlight);
            Fastener(context, new(end.Right - 7, end.Center.Y), edge, highlight);
        }
        // Plate joints continue across the foot rather than one uninterrupted metal strip.
        foreach (var x in new[] { endWidth + 34, w / 2 - 86, w / 2 + 86, w - endWidth - 34 })
        {
            context.DrawLine(new Pen(edge, 1), new(x - 5, bottom.Top + 4), new(x + 5, bottom.Bottom - 4));
            context.DrawLine(new Pen(highlight, .8), new(x - 3, bottom.Top + 4), new(x + 7, bottom.Bottom - 4));
        }
        var latch = new Rect(w / 2 - 68, h - foot + 4, 136, foot - 8);
        Plate(context, latch, 5, metal, edge, highlight);
        Fastener(context, new(latch.Left + 10, latch.Center.Y), edge, highlight);
        Fastener(context, new(latch.Right - 10, latch.Center.Y), edge, highlight);
        for (var i = -1; i <= 1; i++)
        {
            var lamp = new Rect(w / 2 + i * 10 - 2, latch.Center.Y - 2, 4, 4);
            context.DrawRectangle(Recess, new Pen(edge, .5), lamp.Inflate(1.5));
            context.DrawRectangle(accent, null, lamp);
        }
    }

    internal static void DrawPlaque(DrawingContext context, Rect bounds,
        IBrush metal, IBrush inset, IBrush edge, IBrush accent)
    {
        var w = bounds.Width; var h = bounds.Height;
        if (w < 208 || h < 40) return;
        var outline = Polygon([new(.5, 25), new(10, 25), new(27, 2), new(52, 2), new(59, 7),
            new(w - 59, 7), new(w - 52, 2), new(w - 27, 2), new(w - 10, 25), new(w - .5, 25),
            new(w - .5, h - 12), new(w - 12, h - 2), new(12, h - 2), new(.5, h - 12)]);
        using (context.PushTransform(Matrix.CreateTranslation(0, 3)))
            context.DrawGeometry(Recess, new Pen(Shade, 5), outline);
        context.DrawGeometry(metal, new Pen(edge, 1), outline);
        ArmoredWear.Draw(context, outline, bounds);
        context.DrawLine(new Pen(Reflection, 1), new(60, 8), new(w - 60, 8));
        context.DrawLine(new Pen(Reflection, 1), new(10, h - 3), new(w - 10, h - 3));
        // Layered dark receiver; shoulders stand proud of the center display.
        var receiver = new Rect(61, 10, w - 122, h - 20);
        context.DrawGeometry(Recess, new Pen(edge, 1.5), Chamfer(receiver, 7));
        context.DrawGeometry(null, new Pen(Reflection, .8), Chamfer(receiver.Deflate(2), 6));
        context.DrawGeometry(null, new Pen(Shade, 3), Chamfer(receiver.Deflate(4), 5));
        var well = receiver.Deflate(8);
        context.DrawGeometry(inset, new Pen(edge, 1), Chamfer(well, 5));
        context.DrawLine(new Pen(Shade, 3), new(well.Left + 5, well.Top + 2), new(well.Right - 5, well.Top + 2));
        context.DrawLine(new Pen(Reflection, .8), new(well.Left + 5, well.Bottom - 1), new(well.Right - 5, well.Bottom - 1));
        foreach (var left in new[] { true, false })
        {
            var x = left ? 12d : w - 57;
            var collar = new Rect(x, 7, 45, h - 14);
            Plate(context, collar, 6, metal, edge, Reflection);
            var socket = new Rect(left ? x + 22 : x + 5, 19, 18, h - 38);
            context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(socket, 4));
            context.DrawGeometry(null, new Pen(Shade, 2), Chamfer(socket.Deflate(2), 3));
            context.DrawLine(new Pen(Reflection, .8), new(socket.Left + 1, socket.Bottom - 3), new(socket.Left + 1, socket.Top + 3));
            var light = new Rect(socket.Center.X - 2, socket.Top + 5, 4, socket.Height - 10);
            using (context.PushOpacity(.12)) context.DrawRectangle(accent, null, light.Inflate(4), 3, 3);
            using (context.PushOpacity(.28)) context.DrawRectangle(accent, null, light.Inflate(2), 2, 2);
            context.DrawRectangle(accent, null, light, 1.5, 1.5);
            context.DrawLine(new Pen(Reflection, .8), new(light.Left + .7, light.Top + 1), new(light.Left + .7, light.Bottom - 1));
            foreach (var y in new[] { collar.Top + 5, collar.Bottom - 5 })
                Fastener(context, new(left ? collar.Left + 6 : collar.Right - 6, y), edge, Reflection);
            var grooveX = left ? collar.Left + 10 : collar.Right - 10;
            context.DrawLine(new Pen(edge, 2), new(grooveX, collar.Center.Y - 9), new(grooveX, collar.Center.Y + 9));
            context.DrawLine(new Pen(Reflection, .8), new(grooveX + 1.5, collar.Center.Y - 9), new(grooveX + 1.5, collar.Center.Y + 9));
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
        var bevel = Math.Min(3, Math.Min(bounds.Width, bounds.Height) / 4);
        using (context.PushGeometryClip(shape))
        {
            using (context.PushOpacity(.5))
                context.DrawGeometry(highlight, null, Polygon([new(bounds.Left, bounds.Top), new(bounds.Right, bounds.Top),
                    new(bounds.Right - bevel, bounds.Top + bevel), new(bounds.Left + bevel, bounds.Top + bevel)]));
            context.DrawGeometry(Shade, null, Polygon([new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom),
                new(bounds.Right - bevel, bounds.Bottom - bevel), new(bounds.Left + bevel, bounds.Bottom - bevel)]));
            using (context.PushOpacity(.35))
                context.DrawGeometry(highlight, null, Polygon([new(bounds.Left, bounds.Top), new(bounds.Left + bevel, bounds.Top + bevel),
                    new(bounds.Left + bevel, bounds.Bottom - bevel), new(bounds.Left, bounds.Bottom)]));
        }
        context.DrawGeometry(null, new Pen(highlight, .8), Chamfer(bounds.Deflate(1.2), Math.Max(1, cap - 1)));
        context.DrawLine(new Pen(highlight, 1.2), new(bounds.Left + cap, bounds.Top + 2), new(bounds.Right - cap, bounds.Top + 2));
        context.DrawLine(new Pen(Shade, 2), new(bounds.Left + cap, bounds.Bottom - 2), new(bounds.Right - cap, bounds.Bottom - 2));
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
