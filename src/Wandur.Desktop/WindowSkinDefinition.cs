using Avalonia;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;

namespace Wandur.Desktop;

internal sealed record WindowSkinDefinition(string Id, bool CustomChrome,
    double TitleHeight, Thickness FrameInset, double DockHeaderHeight)
{
    internal const double ArmoredBandHeight = 80;
    internal const double ArmoredRailWidth = 24;
    internal const double ArmoredFootHeight = 30;
    private static readonly WindowSkinDefinition Fleet = new(WindowSkinId.Fleet, true, 50, new(6, 0, 6, 6), 38);
    private static readonly WindowSkinDefinition Armored = new(WindowSkinId.Armored, true, ArmoredBandHeight,
        new(ArmoredRailWidth, 0, ArmoredRailWidth, ArmoredFootHeight), 30);
    private static readonly WindowSkinDefinition System = new(WindowSkinId.System, false, 0, default, 30);
    public bool IsArmored => Id == WindowSkinId.Armored;

    public static WindowSkinDefinition Resolve(string? id) => WindowSkinId.Normalize(id) switch
    {
        WindowSkinId.Armored => Armored, WindowSkinId.System => System, _ => Fleet
    };

    public WorldThemeSkin ApplyGeometry(WorldThemeSkin skin) => skin with
    {
        Layout = skin.Layout! with
        {
            TitleBar = skin.Layout!.TitleBar! with { Height = TitleHeight,
                Plaque = skin.Layout.TitleBar!.Plaque! with { Shape = IsArmored ? "armored" : "fleet" } },
            PanelHeader = skin.Layout.PanelHeader! with { Height = DockHeaderHeight }
        },
        Edge = skin.Edge! with { Thickness = FrameInset.Left }
    };
}
