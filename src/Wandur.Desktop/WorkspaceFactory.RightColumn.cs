using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace Wandur.Desktop;

public sealed partial class WorkspaceFactory
{
    /// <summary>
    /// The side columns hold two panels each: Map over Channels on the right, the Workspace over Saved worlds on the
    /// left. When both panels of a column are pinned or hidden, the column used to stay in the layout with
    /// <c>IsCollapsable=false</c>, leaving a grey empty strip. Pull the whole column out of the horizontal layout so
    /// the session document expands; put it back when either panel returns.
    /// </summary>
    public override void HideDockable(IDockable dockable)
    {
        base.HideDockable(dockable);
        if (ColumnOf(dockable) is { } column) SyncColumn(column);
    }

    public override void PinDockable(IDockable dockable)
    {
        // Unpinning reattaches to OriginalOwner; that tool dock must still sit under its column.
        var root = FindRoot(dockable, _ => true);
        var column = ColumnOf(dockable);
        if (column is not null && root is not null && IsDockablePinned(dockable, root))
            EnsureColumnPresent(column);
        base.PinDockable(dockable);
        if (column is not null) SyncColumn(column);
    }

    public override void RestoreDockable(IDockable dockable)
    {
        var column = ColumnOf(dockable);
        if (column is not null) EnsureColumnPresent(column);
        base.RestoreDockable(dockable);
        if (column is not null) SyncColumn(column);
    }

    public override void OnDockableClosed(IDockable? dockable)
    {
        base.OnDockableClosed(dockable);
        if (ColumnOf(dockable) is { } column) SyncColumn(column);
    }

    private ProportionalDock? ColumnOf(IDockable? dockable) =>
        ReferenceEquals(dockable, MapTool) || ReferenceEquals(dockable, ChannelsTool) ? _right
        : ReferenceEquals(dockable, WorldsTool) || ReferenceEquals(dockable, SavedWorldsTool) ? _leftColumn
        : null;

    private (WorkspaceTool?, WorkspaceTool?) ToolsOf(ProportionalDock column) =>
        ReferenceEquals(column, _right) ? (MapTool, ChannelsTool) : (WorldsTool, SavedWorldsTool);

    private static bool ToolInColumn(WorkspaceTool? tool) =>
        tool?.Owner is IDock dock && dock.VisibleDockables?.Contains(tool) == true;

    private void SyncColumn(ProportionalDock column)
    {
        if (_layout is null) return;
        var (first, second) = ToolsOf(column);
        var any = ToolInColumn(first) || ToolInColumn(second);
        var present = _layout.VisibleDockables?.Contains(column) == true;
        if (!any && present)
        {
            if (column.Proportion > 0 && !double.IsNaN(column.Proportion))
                column.CollapsedProportion = column.Proportion;
            RemoveDockable(column, collapse: false);
            CleanupOrphanedSplitters(_layout);
        }
        else if (any && !present) EnsureColumnPresent(column);
    }

    private void EnsureColumnPresent(ProportionalDock column)
    {
        if (_layout is null) return;
        if (_layout.VisibleDockables?.Contains(column) == true) return;
        if (column.CollapsedProportion > 0 && !double.IsNaN(column.CollapsedProportion))
            column.Proportion = column.CollapsedProportion;
        else if (!(column.Proportion > 0) || double.IsNaN(column.Proportion))
            column.Proportion = ReferenceEquals(column, _right) ? 0.23 : 0.18;
        // The left column goes back before the session, the right one after it, each with the splitter that lets it
        // be resized (removing the column left its splitter orphaned, and the cleanup dropped it).
        var splitter = new ProportionalDockSplitter();
        if (ReferenceEquals(column, _leftColumn))
        {
            InsertVisibleDockable(_layout, 0, column);
            InsertVisibleDockable(_layout, 1, splitter);
        }
        else
        {
            AddVisibleDockable(_layout, splitter);
            AddVisibleDockable(_layout, column);
        }
        InitDockable(splitter, _layout);
        InitDockable(column, _layout);
        CleanupOrphanedSplitters(_layout);
    }
}
