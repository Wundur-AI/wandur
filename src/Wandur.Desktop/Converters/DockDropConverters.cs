using Avalonia.Data.Converters;

namespace Wandur.Desktop.Converters;

/// <summary>Converters for the docking guides and drop preview (Styles/DockDrop.axaml).</summary>
public static class DockDropConverters
{
    /// <summary>
    /// True while one of Dock's drop indicators is showing. Dock shows the indicator for the zone it has chosen by
    /// raising that part's opacity from 0 (to 0.5 in Dock 12.1), so the matching guide reads its state from there.
    /// </summary>
    public static readonly IValueConverter IsShown = new FuncValueConverter<double, bool>(opacity => opacity > .01);

    /// <summary>
    /// The title of what is being dragged. A panel dragged by its header is its tool dock, which carries no title of
    /// its own, so the panel showing in it names it; a tab is the dockable itself.
    /// </summary>
    public static readonly IValueConverter Title = new FuncValueConverter<object?, string?>(value => value switch
    {
        Dock.Model.Core.IDock { ActiveDockable.Title: { Length: > 0 } active } => active,
        Dock.Model.Core.IDockable dockable => dockable.Title,
        _ => null,
    });
}
