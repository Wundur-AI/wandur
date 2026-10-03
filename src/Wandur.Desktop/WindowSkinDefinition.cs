using Avalonia;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;

namespace Wandur.Desktop;

/// <summary>A window skin's geometry. A skin with <see cref="TitleBar"/> metrics draws its own chrome; System,
/// with none, keeps the native title bar.</summary>
internal sealed record WindowSkinDefinition(string Id, TitleBarMetrics? TitleBar, Thickness FrameInset, double DockHeaderHeight)
{
    internal const double ArmoredRailWidth = 24;
    internal const double ArmoredFootHeight = 30;
    private static readonly WindowSkinDefinition Fleet = new(WindowSkinId.Fleet, TitleBarMetrics.Fleet, new(6, 0, 6, 6), 38);
    private static readonly WindowSkinDefinition Armored = new(WindowSkinId.Armored, TitleBarMetrics.Armored,
        new(ArmoredRailWidth, 0, ArmoredRailWidth, ArmoredFootHeight), 30);
    private static readonly WindowSkinDefinition System = new(WindowSkinId.System, null, default, 30);
    public bool IsArmored => Id == WindowSkinId.Armored;
    public bool CustomChrome => TitleBar is not null;
    /// <summary>The title band's height, or zero when the system draws the title bar.</summary>
    public double TitleHeight => TitleBar?.BandHeight ?? 0;

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
