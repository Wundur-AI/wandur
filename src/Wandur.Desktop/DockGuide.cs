using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Dock.Model.Core;

namespace Wandur.Desktop;

/// <summary>
/// One docking guide: a small flat tile with a window glyph whose filled part shows where a dragged panel would go,
/// drawn in the theme's colours instead of Dock's stock bitmaps. The compass over a panel uses the split glyphs (half
/// of the window filled, or all of it with a tab for the centre); the guides at the edges of the whole window use the
/// edge glyphs (a narrow bar against that edge, with a tick pointing at it). <see cref="IsLit"/> is bound to Dock's own
/// drop indicator, so the guide under the pointer lights up exactly when Dock has chosen it.
/// Nothing here allocates while a drag moves: the geometry is plain rectangles and lines, and the pens are rebuilt
/// only when a brush changes.
/// </summary>
public sealed class DockGuide : Control
{
    public static readonly StyledProperty<DockOperation> ZoneProperty = AvaloniaProperty.Register<DockGuide, DockOperation>(nameof(Zone), DockOperation.Fill);
    public static readonly StyledProperty<bool> IsEdgeProperty = AvaloniaProperty.Register<DockGuide, bool>(nameof(IsEdge));
    public static readonly StyledProperty<bool> IsLitProperty = AvaloniaProperty.Register<DockGuide, bool>(nameof(IsLit));
    public static readonly StyledProperty<IBrush?> PlateProperty = AvaloniaProperty.Register<DockGuide, IBrush?>(nameof(Plate));
    public static readonly StyledProperty<IBrush?> LitPlateProperty = AvaloniaProperty.Register<DockGuide, IBrush?>(nameof(LitPlate));
    public static readonly StyledProperty<IBrush?> EdgeProperty = AvaloniaProperty.Register<DockGuide, IBrush?>(nameof(Edge));
    public static readonly StyledProperty<IBrush?> GlyphProperty = AvaloniaProperty.Register<DockGuide, IBrush?>(nameof(Glyph));
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<DockGuide, IBrush?>(nameof(Accent));

    /// <summary>The tile's corner radius and the stroke widths, in DIP.</summary>
    public const double Radius = 6, EdgeWidth = 1, LitEdgeWidth = 1.5, GlyphWidth = 1.25;

    static DockGuide()
    {
        AffectsRender<DockGuide>(ZoneProperty, IsEdgeProperty, IsLitProperty, PlateProperty, LitPlateProperty, EdgeProperty, GlyphProperty, AccentProperty);
    }

    public DockOperation Zone { get => GetValue(ZoneProperty); set => SetValue(ZoneProperty, value); }
    public bool IsEdge { get => GetValue(IsEdgeProperty); set => SetValue(IsEdgeProperty, value); }
    public bool IsLit { get => GetValue(IsLitProperty); set => SetValue(IsLitProperty, value); }
    public IBrush? Plate { get => GetValue(PlateProperty); set => SetValue(PlateProperty, value); }
    public IBrush? LitPlate { get => GetValue(LitPlateProperty); set => SetValue(LitPlateProperty, value); }
    public IBrush? Edge { get => GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }
    public IBrush? Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    private static readonly IImmutableSolidColorBrush Shadow = new ImmutableSolidColorBrush(Color.FromArgb(0x48, 0, 0, 0));
    private Pen? _edgePen, _litEdgePen, _glyphPen, _accentPen;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EdgeProperty) _edgePen = null;
        else if (change.Property == GlyphProperty) _glyphPen = null;
        else if (change.Property == AccentProperty) { _litEdgePen = null; _accentPen = null; }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 8 || bounds.Height < 8) return;
        var lit = IsLit;
        var edge = lit ? _litEdgePen ??= Pen(Accent, LitEdgeWidth) : _edgePen ??= Pen(Edge, EdgeWidth);
        var inset = (lit ? LitEdgeWidth : EdgeWidth) / 2;
        // A soft drop shadow lifts the tile off whatever panel it floats over.
        context.DrawRectangle(Shadow, null, bounds.Translate(new Vector(0, 1.5)).Inflate(.5), Radius + 1, Radius + 1);
        context.DrawRectangle(lit ? LitPlate ?? Plate : Plate, edge, bounds.Deflate(inset), Radius, Radius);

        // The window glyph: about 60% of the tile, a little wider than tall, on whole pixels so its hairlines stay crisp.
        var w = Math.Round(bounds.Width * .6);
        var h = Math.Round(bounds.Height * .5);
        var window = new Rect(Math.Round((bounds.Width - w) / 2) + .5, Math.Round((bounds.Height - h) / 2) + .5, w - 1, h - 1);
        var zone = ZoneRect(window, Zone, IsEdge);
        if (zone.Width > 0 && zone.Height > 0) context.FillRectangle(Accent ?? Brushes.Transparent, zone, 1);
        var glyph = _glyphPen ??= Pen(Glyph, GlyphWidth);
        context.DrawRectangle(null, glyph, window, 1.5, 1.5);
        if (Zone == DockOperation.Fill)
        {
            // A document tab on the top edge, as a tabbed panel looks.
            context.DrawLine(glyph, new Point(window.Left + 2, window.Top - 2.5), new Point(window.Left + window.Width * .45, window.Top - 2.5));
        }
        else if (IsEdge)
        {
            // A tick in the tile's margin that points at the window edge this guide docks to.
            var accent = _accentPen ??= Pen(Accent, 2);
            var (from, to) = Zone switch
            {
                DockOperation.Left => (new Point(window.Left - 2, window.Center.Y), new Point(bounds.Left + 4, window.Center.Y)),
                DockOperation.Right => (new Point(window.Right + 2, window.Center.Y), new Point(bounds.Right - 4, window.Center.Y)),
                DockOperation.Top => (new Point(window.Center.X, window.Top - 2), new Point(window.Center.X, bounds.Top + 4)),
                _ => (new Point(window.Center.X, window.Bottom + 2), new Point(window.Center.X, bounds.Bottom - 4)),
            };
            context.DrawLine(accent, from, to);
        }
    }

    /// <summary>The filled part of the glyph: half the window for a split, a quarter strip for a window edge, all of it for a tab.</summary>
    internal static Rect ZoneRect(Rect window, DockOperation zone, bool edge)
    {
        var share = edge ? .3 : .5;
        return zone switch
        {
            DockOperation.Left => new Rect(window.Left, window.Top, window.Width * share, window.Height),
            DockOperation.Right => new Rect(window.Right - window.Width * share, window.Top, window.Width * share, window.Height),
            DockOperation.Top => new Rect(window.Left, window.Top, window.Width, window.Height * share),
            DockOperation.Bottom => new Rect(window.Left, window.Bottom - window.Height * share, window.Width, window.Height * share),
            DockOperation.Fill => window,
            _ => default,
        };
    }

    private static Pen Pen(IBrush? brush, double thickness) => new(brush ?? Brushes.Transparent, thickness);
}
