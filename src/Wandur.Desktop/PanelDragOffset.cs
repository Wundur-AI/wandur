using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Contract;
using Dock.Avalonia.Controls;

namespace Wandur.Desktop;

/// <summary>
/// Where the drag preview sits relative to the pointer. Dock pins a panel dragged by its header to the pointer by its
/// top-left corner; this keeps the point that was grabbed under the pointer instead, as Visual Studio does, so the
/// floating preview starts exactly over the panel and the floating window opens where the preview was. Dock uses the
/// same offset for both, so the preview stays the window a release would make. Tabs keep Dock's own offset.
/// </summary>
internal sealed class PanelDragOffset : IDragOffsetCalculator
{
    public PixelPoint CalculateOffset(Control dragControl, DockControl dockControl, Point pointerPosition)
    {
        var pointer = dockControl.PointToScreen(pointerPosition);
        // The panel is the dockable control around the header: the rectangle Dock sizes the preview and the window from.
        Visual corner = dragControl.Name == "PART_Grip" && dragControl.FindAncestorOfType<ToolChromeControl>()?.FindAncestorOfType<DockableControl>() is { } panel
            ? panel : dragControl;
        return corner.PointToScreen(default) - pointer;
    }
}
