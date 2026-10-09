using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Wandur.Core.Channels;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.ViewModels;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Views;

/// <summary>
/// The docked Channels panel. It mirrors what the classifier recognized: the transcript still holds every
/// line, and this shows a copy of the conversation, one tab per channel, with a box to answer on it.
/// </summary>
public sealed class ChannelsView : UserControl, ISessionPanel
{
    private ChannelMessageList? _messages;
    /// <summary>While another session is in front, the rows wait and catch up when this panel is shown again.</summary>
    public void SetShown(bool shown) => _messages?.SetShown(shown);
    public ChannelsViewModel Model { get; }

    public ChannelsView(ChannelsViewModel model)
    {
        Model = model; DataContext = model;
        Name = "ChannelsView";
        var note = Ui.TextKey(nameof(L.ChannelsMirrorNote), 12, "muted");
        note.Name = "ChannelsMirrorNote";
        note.Margin = new Thickness(24);
        // Before the first message the panel shows this note, centred, and no reply bar, which has nothing to answer yet.
        note.HorizontalAlignment = HorizontalAlignment.Center; note.VerticalAlignment = VerticalAlignment.Center;
        note.TextAlignment = TextAlignment.Center; note.TextWrapping = TextWrapping.Wrap; note.MaxWidth = 260;
        note.Bind(IsVisibleProperty, new Binding(nameof(model.IsMirrorNoteVisible)));
        var tabs = new ListBox
        {
            Name = "ChannelTabs", Background = Brushes.Transparent, Padding = default,
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }),
            ItemTemplate = new FuncDataTemplate<ChannelTabViewModel>((tab, _) => tab is null ? null : TabHeader(tab))
        };
        tabs.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.Tabs)));
        tabs.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(model.SelectedIndex)) { Mode = BindingMode.TwoWay });
        var messages = _messages = new ChannelMessageList(model) { Name = "ChannelMessages" };
        var reply = new TextBox { Name = "ChannelReply", MaxLength = 1024, FontSize = 13, MinHeight = 36, AcceptsReturn = false };
        // The reply bar sits on the transcript surface, so the field takes the terminal's own field chrome
        // and placeholder. With the chrome placeholder it was a light-chrome grey on a dark transcript.
        reply.Classes.Add("terminal-field");
        reply.Bind(TextBox.TextProperty, new Binding(nameof(model.Draft)) { Mode = BindingMode.TwoWay });
        reply.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(model.ReplyHint)));
        reply.Bind(IsEnabledProperty, new Binding(nameof(model.CanReply)));
        reply.Bind(Avalonia.Automation.AutomationProperties.NameProperty, new Binding(nameof(model.ReplyHint)));
        ThemeService.SyncTerminalField(reply);
        ThemeService.Applied += SyncReplyField;
        DetachedFromVisualTree += (_, _) => ThemeService.Applied -= SyncReplyField;
        void SyncReplyField() => ThemeService.SyncTerminalField(reply);
        reply.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            if (model.SendCommand.CanExecute(null)) model.SendCommand.Execute(null);
        };
        var send = new Button { Name = "ChannelSend", Command = model.SendCommand, FontSize = 13, MinHeight = 36, MinWidth = 64, Padding = new Thickness(10, 6) };
        send.Classes.Add("app-button");
        send.Bind(ContentControl.ContentProperty, LocalizedText.Binding(nameof(L.ChannelsSend)));
        Grid.SetColumn(send, 1);
        var replyRow = new Border
        {
            Name = "ChannelReplyBar", Padding = new Thickness(8, 6), BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Children = { reply, send } }
        };
        replyRow.Bind(Border.BackgroundProperty, new DynamicResourceExtension("TerminalBrush"));
        replyRow.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("LineBrush"));
        var header = Ui.Toolbar(tabs, "ChannelsToolbar");
        header.Padding = new Thickness(4, 2);
        var notEmpty = new Binding(nameof(model.IsEmpty)) { Converter = Avalonia.Data.Converters.BoolConverters.Not };
        replyRow.Bind(IsVisibleProperty, notEmpty);
        Grid.SetRow(note, 2); Grid.SetRow(messages, 2); Grid.SetRow(replyRow, 3);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Children = { header, messages, note, replyRow } };
        body.Bind(BackgroundProperty, new DynamicResourceExtension("ChannelBodyBrush"));
        Content = body;
    }

    /// <summary>A tab wears its unread count until the reader looks at it.</summary>
    private static Control TabHeader(ChannelTabViewModel tab)
    {
        var title = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(tab.Title)));
        if (tab.IsPrivate) title.FontWeight = FontWeight.SemiBold;
        var count = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
        count.Bind(TextBlock.TextProperty, new Binding(nameof(tab.UnreadLabel)));
        var badge = new Border
        {
            Name = "ChannelUnread", CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 1),
            VerticalAlignment = VerticalAlignment.Center, Child = count
        };
        badge.Bind(IsVisibleProperty, new Binding(nameof(tab.HasUnread)));
        badge.Bind(Border.BackgroundProperty, new DynamicResourceExtension("AccentBrush"));
        count.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryTextBrush"));
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { title, badge } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Model.Attach(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Model.Detach(); base.OnDetachedFromVisualTree(e); }
}

/// <summary>
/// The messages of the selected tab, drawn with the transcript's own run renderer so a line keeps the color
/// the world gave it. Newest at the bottom, and the view follows new arrivals unless the reader scrolled up.
/// </summary>
internal sealed class ChannelMessageList : Border
{
    private readonly ChannelsViewModel _model;
    private readonly StackPanel _rows = new() { Spacing = 6, Margin = new Thickness(12, 10, 12, 12) };
    private readonly ScrollViewer _viewer;
    internal static readonly StyledProperty<IBrush?> MutedProperty = AvaloniaProperty.Register<ChannelMessageList, IBrush?>("Muted");
    private readonly List<IDisposable> _bindings = [];
    private System.Collections.Specialized.INotifyCollectionChanged? _watched;
    private bool _follow = true;

    public ChannelMessageList(ChannelsViewModel model)
    {
        _model = model;
        _viewer = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Child = _viewer;
        ClipToBounds = true;
        Bind(BackgroundProperty, new DynamicResourceExtension("TerminalBrush"));
        _bindings.Add(this.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("TerminalTextBrush")));
        _bindings.Add(this.Bind(MutedProperty, new DynamicResourceExtension("MutedBrush")));
        TerminalPalette.Bind(this, _bindings);
        _viewer.ScrollChanged += (_, e) =>
        {
            // New rows grow the extent after the follow request ran, which left the newest message half under the reply
            // bar. While following, a grown extent scrolls on to the end; only the reader's own scrolling changes _follow.
            if (_follow && (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)) { _viewer.ScrollToEnd(); return; }
            _follow = _viewer.Offset.Y >= _viewer.Extent.Height - _viewer.Viewport.Height - 2;
        };
    }

    /// <summary>The text on screen, one entry per message.</summary>
    public IReadOnlyList<string> Rows => [.. _rows.Children.OfType<ChannelRow>().Select(row => row.Text)];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _model.PropertyChanged += ModelChanged;
        ThemeService.Applied += ThemeApplied;
        Watch();
        CatchUp();
    }

    private bool _shown = true;

    // A hidden panel takes the new palette when it is shown again (CatchUp sees the generation change).
    private void ThemeApplied() { if (_shown) Rebuild(); }

    /// <summary>A hidden panel (another session in front) adds no rows, which would still cost frames; it catches up,
    /// row by row, when it is shown again.</summary>
    public void SetShown(bool shown)
    {
        if (_shown == shown) return;
        _shown = shown;
        if (shown && _watched is not null) CatchUp();
    }

    /// <summary>
    /// Brings the rows level with the messages after a pause: drops the rows of messages trimmed from the front, refills
    /// the last row if a wrapped tail replaced its message, and appends the rest. Rebuilds only when the rows cannot be
    /// lined up (another tab, a palette change, or more than a whole tab of new messages).
    /// </summary>
    private void CatchUp()
    {
        var messages = _model.Selected.Messages;
        var rows = _rows.Children;
        if (rows.Count == 0 || _builtForTheme != _themeGeneration) { Rebuild(); return; }
        var first = ((ChannelRow)rows[0]).Message;
        var start = -1;
        for (var i = 0; i < messages.Count; i++) if (ReferenceEquals(messages[i], first)) { start = i; break; }
        if (start < 0 || start + rows.Count > messages.Count + 1) { Rebuild(); return; }
        for (var i = 0; i < start; i++) rows.RemoveAt(0);
        for (var i = 0; i < rows.Count; i++)
            if (i < messages.Count && !ReferenceEquals(((ChannelRow)rows[i]).Message, messages[i])) ((ChannelRow)rows[i]).Fill(messages[i], this);
        while (rows.Count > messages.Count) rows.RemoveAt(rows.Count - 1);
        for (var i = rows.Count; i < messages.Count; i++) { var row = new ChannelRow(); row.Fill(messages[i], this); rows.Add(row); }
        FollowIfAtEnd();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _model.PropertyChanged -= ModelChanged;
        ThemeService.Applied -= ThemeApplied;
        if (_watched is not null) _watched.CollectionChanged -= Arrived;
        _watched = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_shown) return;
        if (e.PropertyName is not (nameof(ChannelsViewModel.SelectedIndex) or nameof(ChannelsViewModel.IsEmpty))) return;
        // IsEmpty is announced on every refresh of the model (each output flush). The rows already follow the shown
        // tab's messages through Arrived, so only another tab, or rows that no longer match, need a rebuild.
        var watched = _watched;
        Watch();
        if (e.PropertyName == nameof(ChannelsViewModel.SelectedIndex) || !ReferenceEquals(watched, _watched)
            || _rows.Children.Count != _model.Selected.Messages.Count) Rebuild();
    }

    private void Watch()
    {
        var messages = _model.Selected.Messages;
        if (ReferenceEquals(_watched, messages)) return;
        if (_watched is not null) _watched.CollectionChanged -= Arrived;
        _watched = messages;
        _watched.CollectionChanged += Arrived;
        _follow = true;
    }

    /// <summary>
    /// A message is appended, the oldest is trimmed, or a wrapped tail replaces the last one: each touches one row, so a
    /// full panel (500 messages) is not refilled for every line. Anything else, or rows out of step, rebuilds.
    /// </summary>
    private void Arrived(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_shown) return;
        var messages = _model.Selected.Messages;
        if (!ReferenceEquals(sender, messages)) { Rebuild(); return; }
        var rows = _rows.Children;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { Count: 1 } && e.NewStartingIndex == rows.Count && rows.Count + 1 == messages.Count:
                var row = new ChannelRow();
                row.Fill(messages[e.NewStartingIndex], this);
                rows.Add(row);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is { Count: 1 } && e.OldStartingIndex >= 0 && e.OldStartingIndex < rows.Count && rows.Count == messages.Count + 1:
                rows.RemoveAt(e.OldStartingIndex);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems is { Count: 1 } && e.NewStartingIndex >= 0 && e.NewStartingIndex < rows.Count && rows.Count == messages.Count:
                ((ChannelRow)rows[e.NewStartingIndex]).Fill(messages[e.NewStartingIndex], this);
                break;
            default:
                Rebuild();
                return;
        }
        FollowIfAtEnd();
    }

    // A palette applied while a cached panel was off screen is caught on its return.
    private static int _themeGeneration;
    private int _builtForTheme = -1;
    static ChannelMessageList() => ThemeService.Applied += () => _themeGeneration++;

    private void Rebuild()
    {
        _builtForTheme = _themeGeneration;
        var messages = _model.Selected.Messages;
        while (_rows.Children.Count > messages.Count) _rows.Children.RemoveAt(_rows.Children.Count - 1);
        while (_rows.Children.Count < messages.Count) _rows.Children.Add(new ChannelRow());
        for (var i = 0; i < messages.Count; i++) ((ChannelRow)_rows.Children[i]).Fill(messages[i], this);
        FollowIfAtEnd();
    }

    private void FollowIfAtEnd()
    {
        if (_follow) Avalonia.Threading.Dispatcher.UIThread.Post(_viewer.ScrollToEnd, Avalonia.Threading.DispatcherPriority.Background);
    }

    internal Run Styled(Wandur.Core.Terminal.TextRun run)
    {
        var span = new Run(run.Text);
        if (TerminalPalette.Resolve(this, run.Style.ForegroundIndex, run.Style.Foreground) is { } foreground) span.Foreground = foreground;
        if (TerminalPalette.Resolve(this, run.Style.BackgroundIndex, run.Style.Background) is { } background) span.Background = background;
        if (run.Style.Bold) span.FontWeight = FontWeight.Bold;
        if (run.Style.Italic) span.FontStyle = FontStyle.Italic;
        if (run.Style.Underline) span.TextDecorations = TextDecorations.Underline;
        return span;
    }
}

/// <summary>
/// One channel message: the time in a narrow muted column and the message beside it, so wrapped lines start under the
/// text rather than under the time. Chat is prose, so it is set in the interface font rather than the transcript's
/// monospace, with the world's colors kept on each run.
/// </summary>
internal sealed class ChannelRow : DockPanel
{
    private readonly TextBlock _time = new() { FontSize = 11, Margin = new Thickness(0, 3, 8, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _message = new() { FontSize = 13, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Inlines = [] };

    public ChannelRow()
    {
        SetDock(_time, Avalonia.Controls.Dock.Left);
        Children.Add(_time);
        Children.Add(_message);
    }

    public string Text => _time.Text + " " + string.Concat(_message.Inlines!.OfType<Run>().Select(run => run.Text));
    public ChannelMessage? Message { get; private set; }

    public void Fill(ChannelMessage message, ChannelMessageList owner)
    {
        Message = message;
        _time.Text = message.Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        _time.Foreground = owner.GetValue(ChannelMessageList.MutedProperty);
        var inlines = _message.Inlines!;
        inlines.Clear();
        if (message.Speaker.Length > 0) inlines.Add(new Run(message.Speaker + ": ") { FontWeight = FontWeight.SemiBold });
        foreach (var run in message.Runs) inlines.Add(owner.Styled(run));
        if (message.Runs.Count == 0) inlines.Add(new Run(message.Text));
    }
}
