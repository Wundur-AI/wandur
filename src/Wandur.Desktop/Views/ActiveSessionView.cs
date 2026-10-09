using Avalonia;
using Avalonia.Controls;

namespace Wandur.Desktop.Views;

/// <summary>
/// A tool panel that follows the selected session. Each session's view is built once and kept, hidden, while the
/// session is open, so switching sessions shows a view that is already laid out instead of building the panel (and,
/// for Channels, every message row) again or re-measuring it after a detach; a closed session's view is dropped.
/// </summary>
public sealed class ActiveSessionView(SessionWorkspace sessions, Func<WorkspaceController, Control> build) : ContentControl
{
    private readonly Dictionary<WorkspaceController, Control> _views = [];
    private readonly Panel _host = new();
    private WorkspaceController? _shown;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        sessions.Changed += Refresh;
        Refresh();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        sessions.Changed -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }
    private void Refresh()
    {
        Content ??= _host;
        foreach (var closed in _views.Keys.Where(c => !sessions.Tabs.Any(t => ReferenceEquals(t.Controller, c))).ToArray())
        {
            _host.Children.Remove(_views[closed]);
            _views.Remove(closed);
        }
        if (_shown == sessions.Active.Controller) return;
        _shown = sessions.Active.Controller;
        if (!_views.TryGetValue(_shown, out var view)) { _views[_shown] = view = build(_shown); _host.Children.Add(view); }
        foreach (var other in _host.Children)
        {
            var shown = ReferenceEquals(other, view);
            other.IsVisible = shown;
            if (other is ISessionPanel panel) panel.SetShown(shown);
        }
    }
}

/// <summary>A session panel that can pause its own work while another session's panel is in front.</summary>
public interface ISessionPanel
{
    void SetShown(bool shown);
}
