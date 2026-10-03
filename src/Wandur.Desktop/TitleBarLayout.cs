using Avalonia;

namespace Wandur.Desktop;

internal readonly record struct TitlePlacement(Rect Bounds, bool PlainTitle);

/// <summary>Places a skin's nameplate between the caption controls, from that skin's <see cref="TitleBarMetrics"/>.</summary>
internal static class TitleBarLayout
{
    private static double Safe(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    /// <summary>The width left for the icon and title on a plate between the caption controls, given the
    /// plate's own text inset on each side.</summary>
    public static double TextRoom(double windowWidth, double leftExclusion, double rightExclusion, double textInset)
    {
        var exclusion = Math.Max(Safe(leftExclusion), Safe(rightExclusion)) + 12;
        return Math.Max(0, Safe(windowWidth) - exclusion * 2 - textInset * 2);
    }

    /// <summary>The plaque, centered on the window and kept clear of the wider caption area on both sides. Too
    /// narrow for its shoulders and a short title, it falls back to a plain title.</summary>
    public static TitlePlacement Calculate(TitleBarMetrics metrics, double windowWidth, double leftExclusion,
        double rightExclusion, double measuredTextWidth)
    {
        var w = Safe(windowWidth);
        var exclusion = Math.Max(Safe(leftExclusion), Safe(rightExclusion)) + 12;
        var available = Math.Max(0, w - exclusion * 2);
        // Whole, even widths keep the plaque centered on the pixel grid.
        var width = Math.Min(Math.Ceiling(Safe(measuredTextWidth) / 2) * 2 + metrics.TextInset * 2, available);
        return new(new Rect((w - width) / 2, metrics.PlaqueTop, width, metrics.PlaqueHeight),
            width < metrics.TextInset * 2 + 48);
    }
}
