using Avalonia;

namespace Wandur.Desktop;

internal readonly record struct FleetTitlePlacement(Rect Bounds, bool PlainTitle);

internal static class FleetTitleLayout
{
    // Every Fleet title bar height derives from these. The band is the metal strip the native
    // caption controls sit in; the plaque starts just below its top edge and projects below the
    // seam, into the toolbar's reserved upper ledge.
    public const double BandHeight = 38;
    public const double PlaqueTop = 2;
    public const double PlaqueDrop = 10;
    public const double PlaqueHeight = BandHeight - PlaqueTop + PlaqueDrop;
    // The engraved title and the app icon beside it, sized to the plate inside the plaque.
    public const double TitleFontSize = 15;
    public const double TitleLetterSpacing = 1.2;
    public const double LogoSize = 24;
    // The toolbar row under the band: its top padding clears the plaque's projection, and the rest
    // holds the 32 DIP controls with the same 5 DIP foot as before.
    public const double ToolbarTopPadding = PlaqueDrop + 1;
    public const double ToolbarMinHeight = ToolbarTopPadding + 36 + 5;
    // With the toolbar hidden, the content below still has to clear the projecting plaque.
    public const double HiddenToolbarClearance = PlaqueDrop + 2;
    // The skin, palette and full screen buttons (30 DIP) sit centered in the band.
    public const double ActionButtonSize = 30;
    public const double ActionsTop = (BandHeight - ActionButtonSize) / 2;
    // The dark inset starts 44 DIP inside the outer shoulders. Leave 40 DIP
    // of breathing room inside that inset on each side of the icon and title.
    public const double TextInset = 44 + 40;

    public static FleetTitlePlacement Calculate(double windowWidth, double leftExclusion,
        double rightExclusion, double measuredTextWidth)
    {
        static double Safe(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
        var w = Safe(windowWidth);
        var exclusion = Math.Max(Safe(leftExclusion), Safe(rightExclusion)) + 12;
        var available = Math.Max(0, w - exclusion * 2);
        // Whole, even widths keep the plaque centered on the pixel grid.
        var width = Math.Min(Math.Ceiling(Safe(measuredTextWidth) / 2) * 2 + TextInset * 2, available);
        return new(new Rect((w - width) / 2, PlaqueTop, width, PlaqueHeight), width < TextInset * 2 + 48);
    }
}
