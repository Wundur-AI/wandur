using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Wandur.Core.Discovery;
using Wandur.Desktop.Converters;

namespace Wandur.Desktop;

/// <summary>
/// Nine-slice panel chrome around one ToolDock's live header and body.
/// Passthrough when no panel skin bitmap is ready.
/// </summary>
public sealed class ThemeDockSkinHost : Decorator
{
    public static readonly StyledProperty<bool> IsSkinActiveProperty =
        AvaloniaProperty.Register<ThemeDockSkinHost, bool>(nameof(IsSkinActive));
    public static readonly StyledProperty<Bitmap?> BorderBitmapProperty =
        AvaloniaProperty.Register<ThemeDockSkinHost, Bitmap?>(nameof(BorderBitmap));
    public static readonly StyledProperty<WorldThemeSkinBorder?> BorderMetaProperty =
        AvaloniaProperty.Register<ThemeDockSkinHost, WorldThemeSkinBorder?>(nameof(BorderMeta));
    public static readonly StyledProperty<Thickness> InsetProperty =
        AvaloniaProperty.Register<ThemeDockSkinHost, Thickness>(nameof(Inset));
    public static readonly StyledProperty<double> HeaderHeightProperty =
        AvaloniaProperty.Register<ThemeDockSkinHost, double>(nameof(HeaderHeight), 32);

    public bool IsSkinActive
    {
        get => GetValue(IsSkinActiveProperty);
        private set => SetValue(IsSkinActiveProperty, value);
    }
    public Bitmap? BorderBitmap
    {
        get => GetValue(BorderBitmapProperty);
        private set => SetValue(BorderBitmapProperty, value);
    }
    public WorldThemeSkinBorder? BorderMeta
    {
        get => GetValue(BorderMetaProperty);
        private set => SetValue(BorderMetaProperty, value);
    }
    public Thickness Inset
    {
        get => GetValue(InsetProperty);
        private set => SetValue(InsetProperty, value);
    }
    public double HeaderHeight
    {
        get => GetValue(HeaderHeightProperty);
        private set => SetValue(HeaderHeightProperty, value);
    }

    static ThemeDockSkinHost()
    {
        AffectsRender<ThemeDockSkinHost>(BorderBitmapProperty, BorderMetaProperty, InsetProperty, IsSkinActiveProperty);
        AffectsMeasure<ThemeDockSkinHost>(BorderBitmapProperty, InsetProperty, IsSkinActiveProperty);
        AffectsArrange<ThemeDockSkinHost>(BorderBitmapProperty, InsetProperty, IsSkinActiveProperty);
    }

    public ThemeDockSkinHost()
    {
        DetachedFromVisualTree += (_, _) => { ThemeService.Applied -= OnThemeApplied; LayoutUpdated -= UpdateJoinedEdges; };
        AttachedToVisualTree += (_, _) =>
        {
            ThemeService.Applied += OnThemeApplied;
            LayoutUpdated += UpdateJoinedEdges;
            OnThemeApplied();
        };
    }

    private bool _custom;
    private double _rim;
    private bool _joinsLeft;
    private bool _joinsRight;
    private Thickness ActiveInset => IsSkinActive ? Inset : _custom
        ? new Thickness(_joinsLeft ? 0 : _rim, _rim, _joinsRight ? 0 : _rim, _rim) : default;

    private void UpdateJoinedEdges(object? sender, EventArgs e)
    {
        var left = false;
        var right = false;
        // Physical adjacency matters: a left-aligned dock can be nested in the middle
        // after dragging. Floating windows always retain a complete frame.
        if (_custom && !IsSkinActive && TopLevel.GetTopLevel(this) is MainWindow && Bounds.Width > 0)
        {
            var workspace = this.GetVisualAncestors().OfType<DockControl>().FirstOrDefault(d => d.Name == "WorkspaceDock");
            if (workspace is not null && workspace.Bounds.Width > 0 && this.TranslatePoint(default, workspace) is { } origin)
            {
                left = Math.Abs(origin.X) <= 1.5;
                right = Math.Abs(workspace.Bounds.Width - origin.X - Bounds.Width) <= 1.5;
            }
        }
        if (left == _joinsLeft && right == _joinsRight) return;
        _joinsLeft = left;
        _joinsRight = right;
        Classes.Set("joined-left", left);
        Classes.Set("joined-right", right);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Fallback patterns for borders without a known Dock template. Dock header/body patterns are
    /// derived from their alignment below so Armored's square styles cannot erase Fleet's corners.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, object> Templated = new();

    private void ApplyPanelRadius()
    {
        var radius = ThemeService.ActiveWindowSkin.IsArmored ? 0 : ThemeService.AppliedSkin?.Radii?.Panel;
        if (radius is null) return;
        foreach (var border in this.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PART_Border"))
        {
            // Read Dock's alignment pattern, not the currently styled corners:
            // Armored may already have squared them before this first theme event.
            var part = border.TemplatedParent switch
            {
                ToolChromeControl => "HeaderCorner",
                ToolControl => "ContentCorner",
                _ => null
            };
            var original = part is not null && border.DataContext is IToolDock dock
                ? (CornerRadius)DockChromeConverter.Instance.Convert(dock.Alignment, typeof(CornerRadius), part,
                    System.Globalization.CultureInfo.InvariantCulture)
                : (CornerRadius)Templated.GetValue(border, b => b.CornerRadius);
            border.CornerRadius = new CornerRadius(
                original.TopLeft > 0 ? radius.Value : 0,
                original.TopRight > 0 ? radius.Value : 0,
                original.BottomRight > 0 ? radius.Value : 0,
                original.BottomLeft > 0 ? radius.Value : 0);
        }
    }

    private void OnThemeApplied()
    {
        var definition = ThemeService.ActiveWindowSkin;
        _custom = definition.CustomChrome;
        _rim = definition.IsArmored ? 1 : 2;
        Classes.Set("fleet", _custom);
        Classes.Set("skin-controls", true);
        Classes.Set("system", !_custom);
        Classes.Set("armored", definition.IsArmored);
        // Ui.Toolbar resolves ChromeBrush locally. Share one quiet face with the
        // header so tool rows form a single bay cap, without changing the outer hull.
        foreach (var key in new[] { "ChromeBrush", "ToolbarBrush", "DockHeaderBrush" })
        {
            if (definition.IsArmored && Application.Current?.Resources["PanelBrush"] is IBrush face)
                Resources[key] = face;
            else Resources.Remove(key);
        }
        // Flat headers (System, Fleet) let the header row fill the header's height so the title is centred in it
        // rather than squeezed into the 12 DIP left between the shared 9 DIP top and bottom margins, which cut off
        // descenders. Armored keeps the shared margins.
        if (definition.FlatPanelHeader) Resources["DockToolChromeHeaderMargin"] = new Thickness(12, 0, 6, 0);
        else Resources.Remove("DockToolChromeHeaderMargin");
        HeaderHeight = definition.DockHeaderHeight;
        ApplyPanelRadius();
        var skin = ThemeSkinResources.FromApplied();
        if (skin is not { PanelReady: true } || skin.PanelMeta is not { } meta)
        {
            ClearSkin();
            return;
        }
        BorderBitmap = skin.PanelDefault;
        BorderMeta = meta.Border;
        Inset = new Thickness(meta.Inset.Left, meta.Inset.Top, meta.Inset.Right, meta.Inset.Bottom);
        HeaderHeight = meta.HeaderHeight;
        IsSkinActive = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void ClearSkin()
    {
        BorderBitmap = null;
        BorderMeta = null;
        Inset = default;
        IsSkinActive = false;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inset = ActiveInset;
        // Fall back to plain chrome when the dock cannot fit its border + header.
        if (IsSkinActive && !CanFit(availableSize, inset))
        {
            Child?.Measure(availableSize);
            return Child?.DesiredSize ?? default;
        }
        var width = double.IsInfinity(availableSize.Width) ? availableSize.Width : Math.Max(0, availableSize.Width - inset.Left - inset.Right);
        var height = double.IsInfinity(availableSize.Height) ? availableSize.Height : Math.Max(0, availableSize.Height - inset.Top - inset.Bottom);
        Child?.Measure(new Size(width, height));
        var desired = Child?.DesiredSize ?? default;
        return new Size(desired.Width + inset.Left + inset.Right, desired.Height + inset.Top + inset.Bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var inset = ActiveInset;
        if (IsSkinActive && !CanFit(finalSize, inset))
        {
            Child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
        Child?.Arrange(new Rect(inset.Left, inset.Top,
            Math.Max(0, finalSize.Width - inset.Left - inset.Right),
            Math.Max(0, finalSize.Height - inset.Top - inset.Bottom)));
        return finalSize;
    }

    private bool CanFit(Size size, Thickness inset)
    {
        if (BorderMeta is null) return false;
        var t = BorderMeta.Thickness;
        var minW = t.Left + t.Right;
        var minH = t.Top + t.Bottom;
        if (!double.IsInfinity(size.Width) && size.Width < minW) return false;
        if (!double.IsInfinity(size.Height) && size.Height < minH + HeaderHeight) return false;
        if (!double.IsInfinity(size.Width) && size.Width < inset.Left + inset.Right) return false;
        if (!double.IsInfinity(size.Height) && size.Height < inset.Top + inset.Bottom + HeaderHeight) return false;
        return true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_custom && !IsSkinActive && Bounds.Width > 6 && Bounds.Height > 6)
        {
            if (ThemeService.ActiveWindowSkin.IsArmored)
            {
                // A thin recessed seam instead of the raised Fleet metal surround.
                context.FillRectangle(FleetSkin.RimShadow, new Rect(Bounds.Size));
                context.DrawLine(new Pen(FleetSkin.RimHighlight, .6),
                    new(0, Bounds.Height - .5), new(Bounds.Width, Bounds.Height - .5));
                return;
            }
            var edge = new Pen(FleetSkin.RimEdge, 1);
            if (!_joinsLeft && !_joinsRight)
                context.DrawRectangle(FleetSkin.DockMetal, edge, new Rect(Bounds.Size).Deflate(.5), 3, 3);
            else
            {
                context.FillRectangle(FleetSkin.DockMetal, new Rect(Bounds.Size));
                context.DrawLine(edge, new(0, .5), new(Bounds.Width, .5));
                context.DrawLine(edge, new(0, Bounds.Height - .5), new(Bounds.Width, Bounds.Height - .5));
                if (!_joinsLeft) context.DrawLine(edge, new(.5, 0), new(.5, Bounds.Height));
                if (!_joinsRight) context.DrawLine(edge, new(Bounds.Width - .5, 0), new(Bounds.Width - .5, Bounds.Height));
            }
            // The raised look's highlight along the top; a flat header goes without it.
            if (!ThemeService.ActiveWindowSkin.FlatPanelHeader)
                context.DrawLine(new Pen(FleetSkin.RimHighlight, 1), new(2, 1.5), new(Bounds.Width - 2, 1.5));
        }
        if (!IsSkinActive || BorderBitmap is not { } bitmap || BorderMeta is not { } meta) return;
        var size = Bounds.Size;
        if (!CanFit(size, Inset)) return;
        var patches = ThemeNineSlice.Build(bitmap.PixelSize, meta.Slice, meta.Thickness, size, meta.Tiles);
        ThemeNineSlice.Draw(context, bitmap, patches);
    }
}
