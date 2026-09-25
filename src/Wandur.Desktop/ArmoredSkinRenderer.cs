using Avalonia;
using Avalonia.Media;

namespace Wandur.Desktop;

/// <summary>Clean machined plates. End details stay fixed while straight rails grow with the window.</summary>
internal static class ArmoredSkinRenderer
{
    private static readonly IBrush Shade = Brush.Parse("#48051117");
    private static readonly IBrush Recess = Brush.Parse("#D00B141A");
    private static readonly IBrush Reflection = Brush.Parse("#55FFFFFF");

    internal static void DrawFrame(DrawingContext context, Size size, Rect title,
        IBrush metal, IBrush edge, IBrush highlight, IBrush accent)
    {
        var w = size.Width; var h = size.Height;
        if (w < 32 || h < 80) return;
        var band = 64d;
        context.DrawRectangle(metal, new Pen(edge, 1), new Rect(.5, .5, w - 1, band - 1), 3, 3);
        // Long clean plates terminate at the recessed title shoulders.
        var leftEnd = Math.Clamp(title.Left + 6, 6, w - 6);
        var rightStart = Math.Clamp(title.Right - 6, leftEnd, w - 6);
        foreach (var panel in new[] { new Rect(5, 5, Math.Max(0, leftEnd - 5), band - 11),
                     new Rect(rightStart, 5, Math.Max(0, w - rightStart - 5), band - 11) })
        {
            if (panel.Width < 16) continue;
            context.DrawGeometry(null, new Pen(edge, 1), Chamfer(panel, 4));
            context.DrawLine(new Pen(highlight, 1), new(panel.Left + 5, panel.Top + 1), new(panel.Right - 5, panel.Top + 1));
            context.DrawLine(new Pen(highlight, 1), new(panel.Left + 4, panel.Bottom + 2), new(panel.Right - 4, panel.Bottom + 2));
        }
        context.FillRectangle(metal, new Rect(0, band, 8, h - band));
        context.FillRectangle(metal, new Rect(w - 8, band, 8, h - band));
        context.FillRectangle(metal, new Rect(0, h - 8, w, 8));
        foreach (var x in new[] { .5, w - .5, 7.5, w - 7.5 })
            context.DrawLine(new Pen(edge, 1), new(x, band), new(x, h - 3));
        foreach (var x in new[] { 2d, w - 5 })
        {
            var rail = new Rect(x, band + 3, 3, Math.Max(0, h - band - 14));
            context.DrawRectangle(Recess, null, rail.Inflate(1), 1, 1);
            using (context.PushOpacity(.22)) context.DrawRectangle(accent, null, rail.Inflate(1.5), 2, 2);
            context.DrawRectangle(accent, null, rail, 1, 1);
            using (context.PushOpacity(.55)) context.FillRectangle(highlight, new Rect(x, rail.Top, 1, rail.Height));
        }
        context.DrawLine(new Pen(edge, 1), new(7, h - 7.5), new(w - 7, h - 7.5));
        context.DrawLine(new Pen(highlight, 1), new(8, h - 6.5), new(w - 8, h - 6.5));
        context.DrawLine(new Pen(edge, 1), new(4, h - .5), new(w - 4, h - .5));
        // Small machined foot details live entirely in the reserved bottom rail.
        foreach (var x in new[] { 12d, w - 52 })
        {
            var foot = new Rect(x, h - 7, 40, 6);
            context.DrawGeometry(metal, new Pen(edge, 1), Chamfer(foot, 2));
            for (var i = 0; i < 6; i++)
                context.DrawLine(new Pen(Recess, 2), new(x + 7 + i * 5, h - 5), new(x + 7 + i * 5, h - 2));
        }
    }

    internal static void DrawPlaque(DrawingContext context, Rect bounds,
        IBrush metal, IBrush inset, IBrush edge, IBrush accent)
    {
        var w = bounds.Width; var h = bounds.Height;
        if (w < 180 || h < 40) return;
        var outline = Polygon([new(0.5, 20), new(12, 20), new(20, 2), new(w - 20, 2),
            new(w - 12, 20), new(w - .5, 20), new(w - .5, h - 9), new(w - 9, h - 2),
            new(9, h - 2), new(.5, h - 9)]);
        using (context.PushTransform(Matrix.CreateTranslation(0, 2)))
            context.DrawGeometry(Shade, new Pen(Shade, 3), outline);
        context.DrawGeometry(metal, new Pen(edge, 1), outline);
        context.DrawLine(new Pen(Reflection, 1), new(21, 3), new(w - 21, 3));
        context.DrawLine(new Pen(Reflection, 1), new(1.5, 22), new(1.5, h - 10));
        var well = new Rect(32, 9, w - 64, h - 18);
        context.DrawGeometry(Recess, new Pen(edge, 1), Chamfer(well, 5));
        context.DrawGeometry(inset, new Pen(Reflection, 1), Chamfer(well.Deflate(3), 4));
        context.DrawLine(new Pen(Shade, 3), new(well.Left + 6, well.Top + 5), new(well.Right - 6, well.Top + 5));
        context.DrawLine(new Pen(Reflection, 1), new(well.Left + 6, well.Bottom - 2), new(well.Right - 6, well.Bottom - 2));
        foreach (var x in new[] { 19d, w - 24 })
        {
            var lamp = new Rect(x, 21, 5, h - 39);
            context.DrawRectangle(Recess, new Pen(edge, 1), lamp.Inflate(3), 3, 3);
            using (context.PushOpacity(.20)) context.DrawRectangle(accent, null, lamp.Inflate(2), 3, 3);
            context.DrawRectangle(accent, null, lamp, 2, 2);
            context.DrawLine(new Pen(Reflection, 1), new(x + 1, lamp.Top + 1), new(x + 1, lamp.Bottom - 1));
        }
        foreach (var point in new[] { new Point(11, h - 12), new Point(w - 11, h - 12), new Point(25, 9), new Point(w - 25, 9) })
        {
            context.DrawEllipse(edge, null, point, 1.4, 1.4);
            context.DrawLine(new Pen(Reflection, .6), point + new Vector(-.8, .5), point + new Vector(.8, .5));
        }
    }

    internal static IBrush CreateToolbar(Size size, Rect title, IBrush metal, IBrush edge, IBrush highlight)
    {
        if (size.Width < 2 || size.Height < 2) return metal;
        var left = Math.Clamp(title.Left - 5, 0, size.Width);
        var right = Math.Clamp(title.Right + 5, left, size.Width);
        var bottom = Math.Clamp(title.Bottom + 4, 3, Math.Max(3, size.Height - 1));
        var cap = Math.Min(8, (right - left) / 2);
        Point[] rim = title.Width >= 180 && title.Bottom > 0
            ? [new(0, 2), new(left, 2), new(left, bottom - cap), new(left + cap, bottom),
               new(right - cap, bottom), new(right, bottom - cap), new(right, 2), new(size.Width, 2)]
            : [new(0, 2), new(size.Width, 2)];
        var face = Polygon([.. rim, new(size.Width, size.Height), new(0, size.Height)]);
        var seam = Polygon(rim, false);
        var drawing = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(size)) };
        drawing.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(size)), Brush = edge });
        drawing.Children.Add(new GeometryDrawing { Geometry = face, Brush = metal });
        drawing.Children.Add(new GeometryDrawing { Geometry = seam, Pen = new Pen(Shade, 5) });
        drawing.Children.Add(new GeometryDrawing { Geometry = seam, Pen = new Pen(edge, 1) });
        var shine = new DrawingGroup { Transform = new TranslateTransform(0, 1.5) };
        shine.Children.Add(new GeometryDrawing { Geometry = seam, Pen = new Pen(highlight, 1) });
        drawing.Children.Add(shine);
        return new DrawingBrush { Drawing = drawing, Stretch = Stretch.Fill,
            SourceRect = new RelativeRect(new Rect(size), RelativeUnit.Absolute) };
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
