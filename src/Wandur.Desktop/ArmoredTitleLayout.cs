using Avalonia;

namespace Wandur.Desktop;

internal static class ArmoredTitleLayout
{
    public const double PlaqueHeight = 70;
    public const double TextInset = 100;

    public static FleetTitlePlacement Calculate(double windowWidth, double leftExclusion,
        double rightExclusion, double measuredTextWidth)
    {
        static double Safe(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
        var width = Safe(windowWidth);
        var reserve = Math.Max(Safe(leftExclusion), Safe(rightExclusion)) + 12;
        var available = Math.Max(0, width - reserve * 2);
        var plate = Math.Min(Safe(measuredTextWidth) + TextInset * 2, available);
        return new(new Rect((width - plate) / 2, 2, plate, PlaqueHeight), plate < TextInset * 2 + 48);
    }
}
