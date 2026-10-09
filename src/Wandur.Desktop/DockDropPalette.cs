using Avalonia;
using Avalonia.Media;

namespace Wandur.Desktop;

/// <summary>
/// The colours and sizes of the docking guides and the drop preview (Styles/DockDrop.axaml), all taken from the theme
/// in force so a drag looks the same in every skin and follows a world's or a custom theme's accent.
/// </summary>
internal static class DockDropPalette
{
    /// <summary>How much of the accent the preview fill and its edge show, as the eye sees them.</summary>
    public const double FillAlpha = .3, EdgeAlpha = .5;

    /// <summary>
    /// The opacity Dock 12.1 gives the drop indicator it has chosen (DockTargetBase sets it on the template part). The
    /// indicator brushes carry the visible alpha divided by this, so the preview over a panel matches the floating one.
    /// </summary>
    public const double IndicatorOpacity = .5;

    /// <summary>A guide tile, the gap between the tiles of the compass, and the window-edge guides' distance from the edge.</summary>
    public const double GuideSize = 34, GuideSpacing = 4, GuideEdgeMargin = 10;

    /// <summary>
    /// The share of the window a panel docked against one of its edges takes. Dock's default is half, which turns a
    /// side panel into half the window; a quarter is close to the panels the default layout has. Read by Dock when it
    /// builds the window-edge indicators and when it sizes the dock a drop creates, so it is set before either.
    /// </summary>
    public const double WindowEdgeProportion = .25;

    public static void Apply(ThemeResources resources, Color accent, Color panel, Color muted, Color line, Color text)
    {
        resources.Color("DockDropFillBrush", WithAlpha(accent, FillAlpha));
        resources.Color("DockDropEdgeBrush", WithAlpha(accent, EdgeAlpha));
        resources.Color("DockDropIndicatorFillBrush", WithAlpha(accent, FillAlpha / IndicatorOpacity));
        resources.Color("DockDropIndicatorEdgeBrush", WithAlpha(accent, EdgeAlpha / IndicatorOpacity));
        resources.Color("DockDropCaptionBrush", WithAlpha(panel, .92));
        resources.Color("DockDropCaptionTextBrush", text);
        resources.Color("DockGuidePlateBrush", WithAlpha(Mix(panel, text, .07), .97));
        resources.Color("DockGuideLitPlateBrush", Mix(Mix(panel, text, .07), accent, .25));
        resources.Color("DockGuideEdgeBrush", Mix(line, text, .3));
        resources.Color("DockGuideGlyphBrush", muted);
        resources.Value("DockGuideSize", GuideSize);
        resources.Value("DockGuideSpacing", GuideSpacing);
        resources.Value("DockGuideEdgeMargin", new Thickness(GuideEdgeMargin));
    }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
