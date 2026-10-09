using Avalonia.Controls;
using Avalonia.Layout;
using Dock.Model.Core;
using Dock.Model.Controls;

namespace Wandur.Desktop;

public sealed partial class WorkspaceFactory
{
    private SplitSize? _pendingSplit;

    /// <summary>
    /// A panel dropped beside another, or against an edge of the window, lands at the size its preview showed. The
    /// preview (Styles/DockDrop.axaml) is half of the panel under the pointer, or a strip of
    /// <see cref="DockDropPalette.WindowEdgeProportion"/> of the window for an edge guide, measured before the drop.
    /// Dock sizes the new dock by proportions of its parent, but the drop first takes the dragged panel out of its old
    /// place, and when that empties a column or a row the parent grows, so the new panel came out wider (or taller)
    /// than its preview: dropping the Workspace on an empty right edge took half the window. The share is set here as
    /// before, and once the new layout has been measured the pair is corrected so the new panel has the previewed
    /// extent in pixels and the panel it split from takes the rest; no other panel moves.
    /// </summary>
    public override void SplitToDock(IDock dock, IDockable dockable, DockOperation operation)
    {
        var windowEdge = dock.Owner is IRootDock && operation is DockOperation.Left or DockOperation.Right or DockOperation.Top or DockOperation.Bottom;
        var horizontal = operation is DockOperation.Left or DockOperation.Right;
        dock.GetVisibleBounds(out _, out _, out var width, out var height);
        var previewed = (horizontal ? width : height) * (windowEdge ? DockDropPalette.WindowEdgeProportion : .5);
        base.SplitToDock(dock, dockable, operation);
        if (dock.Owner is not IProportionalDock { VisibleDockables: { } children } container) return;
        var placed = children.FirstOrDefault(child => ReferenceEquals(child, dockable) || Contains(child, dockable));
        if (placed is null || ReferenceEquals(placed, dock)) return;
        if (windowEdge)
        {
            placed.Proportion = placed.CollapsedProportion = DockDropPalette.WindowEdgeProportion;
            dock.Proportion = dock.CollapsedProportion = 1 - DockDropPalette.WindowEdgeProportion;
        }
        if (previewed > 0 && !double.IsNaN(previewed)) KeepPreviewedSize(new SplitSize(container, placed, dock, previewed, horizontal));
    }

    private static bool Contains(IDockable parent, IDockable dockable) =>
        parent is IDock { VisibleDockables: { } children } && children.Any(child => ReferenceEquals(child, dockable) || Contains(child, dockable));

    /// <summary>The new dock of a split, the dock it was split from, and how many pixels the preview gave it.</summary>
    private sealed record SplitSize(IProportionalDock Container, IDockable Placed, IDockable Sibling, double Extent, bool Horizontal)
    {
        public int Passes { get; set; }
    }

    private void KeepPreviewedSize(SplitSize split)
    {
        StopKeepingSize();
        _pendingSplit = split;
        foreach (var control in DockControls.OfType<Layoutable>()) control.LayoutUpdated += SplitLaidOut;
    }

    private void StopKeepingSize()
    {
        _pendingSplit = null;
        foreach (var control in DockControls.OfType<Layoutable>()) control.LayoutUpdated -= SplitLaidOut;
    }

    private void SplitLaidOut(object? sender, EventArgs args)
    {
        if (_pendingSplit is not { } split) { StopKeepingSize(); return; }
        // The new docks get their controls over the next layout pass or two, and the window may settle its chrome
        // over another, so the pair is checked after each pass until the new panel has its extent. Give up after a
        // few passes, or if the docks went elsewhere in the meantime.
        if (++split.Passes > 8 || split.Placed.Owner != split.Container || split.Sibling.Owner != split.Container) { StopKeepingSize(); return; }
        if (!TryExtent(split.Placed, split.Horizontal, out var placed) || !TryExtent(split.Sibling, split.Horizontal, out var sibling)) return;
        var share = split.Placed.Proportion + split.Sibling.Proportion;
        var total = placed + sibling;
        if (double.IsNaN(share) || share <= 0 || total <= split.Extent) { StopKeepingSize(); return; }
        if (Math.Abs(placed - split.Extent) < 1) { StopKeepingSize(); return; }
        var proportion = share * split.Extent / total;
        split.Placed.Proportion = split.Placed.CollapsedProportion = proportion;
        split.Sibling.Proportion = split.Sibling.CollapsedProportion = share - proportion;
    }

    private bool TryExtent(IDockable dockable, bool horizontal, out double extent)
    {
        extent = 0;
        if (!VisibleDockableControls.TryGetValue(dockable, out var tracked) || tracked is not Control { IsEffectivelyVisible: true } control) return false;
        extent = horizontal ? control.Bounds.Width : control.Bounds.Height;
        return extent > 0;
    }
}
