namespace Wandur.Desktop;

/// <summary>
/// Every size a custom-chrome skin chooses for its title bar: the band the native caption controls sit in, the
/// nameplate that starts just below the band's top edge and projects below its seam, the engraved title and icon on
/// that plate, the toolbar row under the band and the caption action buttons. The shapes stay with each skin's
/// renderer; only the numbers live here, so a new skin needs a new instance and nothing else for its sizes.
/// </summary>
internal sealed record TitleBarMetrics
{
    /// <summary>The metal band across the top of the window, and the native title bar height hint.</summary>
    public required double BandHeight { get; init; }
    /// <summary>Where the plaque starts, below the top of the band.</summary>
    public required double PlaqueTop { get; init; }
    /// <summary>How far the plaque projects below the band's seam, into the toolbar's upper ledge.</summary>
    public required double PlaqueDrop { get; init; }
    public double PlaqueHeight => BandHeight - PlaqueTop + PlaqueDrop;

    public required double TitleFontSize { get; init; }
    public required double TitleLetterSpacing { get; init; }
    /// <summary>The app icon beside the title on the plate.</summary>
    public required double LogoSize { get; init; }
    /// <summary>The room kept clear on each side of the icon and title inside the plaque, for its shoulders.</summary>
    public required double TextInset { get; init; }
    /// <summary>Whether the plaque width is rounded up to a whole, even number so it centers on the pixel grid.</summary>
    public required bool EvenPlaqueWidth { get; init; }

    /// <summary>The toolbar row's top padding, which clears the plaque's projection.</summary>
    public required double ToolbarTopPadding { get; init; }
    public required double ToolbarMinHeight { get; init; }
    /// <summary>With the toolbar hidden, the content below still has to clear the projecting plaque.</summary>
    public required double HiddenToolbarClearance { get; init; }

    /// <summary>The skin, palette and full screen buttons beside the plate.</summary>
    public required double ActionButtonSize { get; init; }
    private readonly double? _actionsTop;
    /// <summary>The top of the caption action buttons: centered in the band unless a skin sets it.</summary>
    public double ActionsTop { get => _actionsTop ?? (BandHeight - ActionButtonSize) / 2; init => _actionsTop = value; }

    /// <summary>Fleet: a 38 DIP band with a plaque projecting 10 DIP below it. The toolbar row holds the 32 DIP
    /// controls with a 5 DIP foot below and 1 DIP of air above, under the plaque.</summary>
    public static readonly TitleBarMetrics Fleet = new()
    {
        BandHeight = 38, PlaqueTop = 2, PlaqueDrop = 10,
        TitleFontSize = 13, TitleLetterSpacing = 1.0, LogoSize = 24,
        // The dark inset starts 44 DIP inside the outer shoulders. Leave 40 DIP of breathing room inside that
        // inset on each side of the icon and title.
        TextInset = 44 + 40, EvenPlaqueWidth = true,
        ToolbarTopPadding = 10 + 1, ToolbarMinHeight = 10 + 1 + 36 + 5, HiddenToolbarClearance = 10 + 2,
        ActionButtonSize = 30,
    };

    /// <summary>Armored: an 80 DIP band with a deeper plaque whose shoulders and side lights sit outside a 100 DIP
    /// text inset.</summary>
    public static readonly TitleBarMetrics Armored = new()
    {
        BandHeight = 80, PlaqueTop = 2, PlaqueDrop = 8,
        TitleFontSize = 20, TitleLetterSpacing = 1.8, LogoSize = 32,
        TextInset = 100, EvenPlaqueWidth = false,
        ToolbarTopPadding = 13, ToolbarMinHeight = 13 + 36 + 5, HiddenToolbarClearance = 14,
        ActionButtonSize = 30, ActionsTop = 10,
    };
}
