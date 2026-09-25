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
            // A single structural wing, not small hatches applied to a flat header.
            var left = panel.Left < w / 2;
            Point[] wing = [new(panel.Left + 7, panel.Top), new(panel.Right - 26, panel.Top),
                new(panel.Right - 16, panel.Top + 11), new(panel.Right, panel.Top + 11),
                new(panel.Right, panel.Bottom - 11), new(panel.Right - 12, panel.Bottom),
                new(panel.Left + 7, panel.Bottom), new(panel.Left, panel.Bottom - 7), new(panel.Left, panel.Top + 7)];
            if (!left) wing = Mirror(wing, panel.Left + panel.Right);
            SculptedPlate(context, wing, 8, metal, edge, highlight);
            var y = panel.Bottom - 14;
            // Recessed stepped joint between the wing and its continuous lower stiffener.
            Point[] joint = [new(panel.Left + 4, y), new(panel.Right - 48, y),
                new(panel.Right - 40, y + 5), new(panel.Right - 5, y + 5)];
            if (!left) joint = Mirror(joint, panel.Left + panel.Right);
            context.DrawGeometry(null, new Pen(Recess, 2.5), Polygon(joint, false));
            using (context.PushTransform(Matrix.CreateTranslation(0, 2)))
                context.DrawGeometry(null, new Pen(highlight, 1), Polygon(joint, false));
            if (panel.Width > 240)
            {
                var seamX = left ? panel.Right - 100 : panel.Left + 100;
                if (seamX - 12 >= captionExclusion.Left + 4 && seamX + 12 <= w - captionExclusion.Right - 4)
                {
                    var direction = left ? 1 : -1;
                    var seam = Polygon([new(seamX, panel.Top + 9), new(seamX, y - 18),
                        new(seamX + direction * 9, y - 10), new(seamX + direction * 9, y)], false);
                    context.DrawGeometry(null, new Pen(Recess, 2), seam);
                    using (context.PushTransform(Matrix.CreateTranslation(1.5, 0)))
                        context.DrawGeometry(null, new Pen(highlight, .8), seam);
                    Fastener(context, new(seamX - direction * 6, y - 6), edge, highlight);
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
            var innerLip = x == 0 ? rail - 3 : w - rail + 3;
            context.DrawLine(new Pen(edge, 4), new(innerLip, band), new(innerLip, h - foot));
            context.DrawLine(new Pen(highlight, 1), new(innerLip + (x == 0 ? -2 : 2), band),
                new(innerLip + (x == 0 ? -2 : 2), h - foot));
            // Fixed-size inspection joints. Only the straight rail and light channel extend.
            foreach (var fraction in new[] { .0, .34, .70 })
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
        context.DrawLine(new Pen(Shade, 3), new(rail, bottom.Top + 2), new(w - rail, bottom.Top + 2));
        context.DrawLine(new Pen(highlight, 1), new(rail, bottom.Top + 3), new(w - rail, bottom.Top + 3));
        var endWidth = Math.Min(138, (w - 24) / 3);
        foreach (var x in new[] { 5d, w - endWidth - 5 })
        {
            var end = new Rect(x, h - foot + 3, endWidth, foot - 6);
            // The vertical rail turns into the foot as one casting. Its inner notch
            // remains outside the live content rectangle, even at small window sizes.
            Point[] corner = [new(2, h - 76), new(21, h - 76), new(21, h - 31),
                new(24, h - 30), new(28, h - 27), new(endWidth + 1, h - 27),
                new(endWidth + 8, h - 20), new(endWidth + 8, h - 7),
                new(endWidth + 2, h - 1), new(9, h - 1), new(1, h - 9), new(1, h - 68)];
            if (x != 5) corner = Mirror(corner, w);
            SculptedPlate(context, corner, 4, metal, edge, highlight);
            Fastener(context, new(x == 5 ? 14 : w - 14, h - 65), edge, highlight);
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
        // A continuous inset light passes through the rail and the corner casting.
        foreach (var x in new[] { 2d, w - 5 })
        {
            var light = new Rect(x, band + 4, 3, Math.Max(0, h - band - foot - 4));
            context.DrawRectangle(Recess, new Pen(edge, .6), light.Inflate(1), 1, 1);
            using (context.PushOpacity(.22)) context.DrawRectangle(accent, null, light.Inflate(2), 2, 2);
            context.DrawRectangle(accent, null, light, 1, 1);
            using (context.PushOpacity(.65)) context.FillRectangle(highlight, new Rect(light.X, light.Y, .7, light.Height));
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
        var receiver = new Rect(70, 10, w - 140, h - 20);
        context.DrawGeometry(Recess, new Pen(edge, 1.5), Chamfer(receiver, 7));
        context.DrawGeometry(null, new Pen(Reflection, .8), Chamfer(receiver.Deflate(2), 6));
        context.DrawGeometry(null, new Pen(Shade, 3), Chamfer(receiver.Deflate(4), 5));
        var well = receiver.Deflate(8);
        context.DrawGeometry(inset, new Pen(edge, 1), Chamfer(well, 5));
        context.DrawLine(new Pen(Shade, 3), new(well.Left + 5, well.Top + 2), new(well.Right - 5, well.Top + 2));
        context.DrawLine(new Pen(Reflection, .8), new(well.Left + 5, well.Bottom - 1), new(well.Right - 5, well.Bottom - 1));
        foreach (var left in new[] { true, false })
        {
            // The light is mounted on a dark spine behind two overlapping armor jaws.
            // Keep these fixed-width components outside the title's 100 DIP text inset.
            var socket = new Rect(left ? 43 : w - 67, 14, 24, h - 28);
            context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(socket, 4));
            context.DrawGeometry(null, new Pen(Shade, 2), Chamfer(socket.Deflate(2), 3));
            context.DrawLine(new Pen(Reflection, .8), new(socket.Left + 1, socket.Bottom - 3), new(socket.Left + 1, socket.Top + 3));
            var lightInset = Math.Min(7, socket.Height / 4);
            var light = new Rect(socket.Center.X - 2, socket.Top + lightInset, 4, socket.Height - lightInset * 2);
            using (context.PushOpacity(.12)) context.DrawRectangle(accent, null, light.Inflate(4), 3, 3);
            using (context.PushOpacity(.28)) context.DrawRectangle(accent, null, light.Inflate(2), 2, 2);
            context.DrawRectangle(accent, null, light, 1.5, 1.5);
            context.DrawLine(new Pen(Reflection, .8), new(light.Left + .7, light.Top + 1), new(light.Left + .7, light.Bottom - 1));
            Point[] shoulder = [new(26, 3), new(44, 3), new(50, 10), new(50, h * .28),
                new(43, h * .36), new(43, h * .66), new(49, h * .74), new(49, h - 8),
                new(18, h - 8), new(9, h - 17), new(9, 27)];
            if (!left) shoulder = Mirror(shoulder, w);
            SculptedPlate(context, shoulder, 4, metal, edge, Brushes.White);
            Point[] heel = [new(9, h - 30), new(25, h - 30), new(32, h - 23),
                new(49, h - 23), new(49, h - 7), new(18, h - 7), new(9, h - 16)];
            if (!left) heel = Mirror(heel, w);
            SculptedPlate(context, heel, 2.5, metal, edge, Brushes.White);
            foreach (var y in new[] { 12d, h - 15 })
                Fastener(context, new(left ? 32 : w - 32, y), edge, Reflection);
            // Small locking tabs bridge the spine to the receiver without boxing in the LED.
            foreach (var y in new[] { 7d, h - 14 })
                Plate(context, new Rect(left ? 57 : w - 81, y, 24, 7), 2, metal, edge, Reflection);
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

    private static Point[] Mirror(Point[] points, double axisSum) =>
        points.Select(p => new Point(axisSum - p.X, p.Y)).Reverse().ToArray();

    /// <summary>Broad bevels lit from the upper left, independent of a plate's handedness.</summary>
    private static void SculptedPlate(DrawingContext context, Point[] outline, double depth,
        IBrush metal, IBrush edge, IBrush highlight)
    {
        var shape = Polygon(outline);
        context.DrawGeometry(metal, new Pen(edge, 1), shape);
        var bounds = new Rect(new Point(outline.Min(p => p.X), outline.Min(p => p.Y)),
            new Point(outline.Max(p => p.X), outline.Max(p => p.Y)));
        ArmoredWear.Draw(context, shape, bounds);
        using (context.PushGeometryClip(shape))
        {
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Length];
                var delta = new Vector(b.X - a.X, b.Y - a.Y);
                var length = delta.Length;
                if (length < .01) continue;
                var inward = new Vector(-delta.Y, delta.X) / length * depth;
                var light = (-.6 * delta.Y + .8 * delta.X) / length;
                using (context.PushOpacity(Math.Abs(light) * (light > 0 ? .75 : .48)))
                    context.DrawGeometry(light > 0 ? highlight : Recess, null,
                        Polygon([a, b, b + inward, a + inward]));
            }
        }
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
