using Dock.Model.Core;
using Dock.Model.Controls;

namespace Wandur.Desktop;

public sealed partial class WorkspaceFactory
{
    /// <summary>
    /// A panel dropped on one of the window-edge guides lands as the strip its preview showed: the guide's indicator
    /// takes <see cref="DockDropPalette.WindowEdgeProportion"/> of the window (Styles/DockDrop.axaml), and so does the
    /// dock the drop creates. Dock splits the window's top-level dock for an edge drop and leaves both halves without a
    /// proportion, which shares the window equally, so the new side is sized here. Splits inside the window (a guide
    /// over one panel) keep Dock's halves, which is what their preview shows.
    /// </summary>
    public override void SplitToDock(IDock dock, IDockable dockable, DockOperation operation)
    {
        var windowEdge = dock.Owner is IRootDock && operation is DockOperation.Left or DockOperation.Right or DockOperation.Top or DockOperation.Bottom;
        base.SplitToDock(dock, dockable, operation);
        if (!windowEdge || dock.Owner is not IProportionalDock { VisibleDockables: { } children }) return;
        var placed = children.FirstOrDefault(child => ReferenceEquals(child, dockable) || Contains(child, dockable));
        if (placed is null || ReferenceEquals(placed, dock)) return;
        placed.Proportion = placed.CollapsedProportion = DockDropPalette.WindowEdgeProportion;
        dock.Proportion = dock.CollapsedProportion = 1 - DockDropPalette.WindowEdgeProportion;
    }

    private static bool Contains(IDockable parent, IDockable dockable) =>
        parent is IDock { VisibleDockables: { } children } && children.Any(child => ReferenceEquals(child, dockable) || Contains(child, dockable));
}
