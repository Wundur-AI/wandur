using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using XTerm.Buffer;

namespace Wandur.Desktop.Terminal;

/// <summary>Adapts text blinking to a transcript with a separate command input.</summary>
internal sealed class MudTerminalSurface : Iciclecreek.Terminal.TerminalView
{
    private readonly DispatcherTimer _blinkTimer;
    private bool _allowBlink, _hiddenPhase, _attached, _sessionInitialized;

    public MudTerminalSurface()
    {
        CursorBlink = false;
        SuppressCursor = true;
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blinkTimer.Tick += (_, _) => { _hiddenPhase = !_hiddenPhase; InvalidateVisual(); };
    }

    public void InitializeSession() => OnInitialized();
    protected override void OnInitialized()
    {
        if (_sessionInitialized) return;
        _sessionInitialized = true;
        base.OnInitialized();
    }

    public bool AllowBlink
    {
        get => _allowBlink;
        set { if (_allowBlink == value) return; _allowBlink = value; _hiddenPhase = false; UpdateTimer(); InvalidateVisual(); }
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers == primary && e.Key == Key.A)
        {
            e.Handled = true; Terminal.Selection.SelectAll(); InvalidateVisual(); return;
        }
        if ((e.KeyModifiers == primary || e.KeyModifiers == (primary | KeyModifiers.Shift)) && e.Key == Key.C)
        {
            e.Handled = true; await CopyAsync(); return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>A right click: the logical line under the pointer (wrapped rows joined) and the selection, if any.</summary>
    public event Action<string?, string?>? MenuRequested;

    /// <summary>
    /// The library answers a right click by copying the selection or pasting the clipboard into the terminal,
    /// which is not what a transcript wants. The click is taken here, before the library sees it, and turned
    /// into a request for the transcript's own menu; the selection stays where the reader made it.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        _pendingCommandLink = null;
        // The library opens links on Control+click only, which on macOS is the secondary click habit; Cmd+click is
        // the Mac convention, so it is taken here with the same press-and-release-on-the-same-link rule.
        if (CommandClickOpensLinks && e.KeyModifiers == KeyModifiers.Meta && point.Properties.IsLeftButtonPressed &&
            LinkAt(point.Position) is { } link)
        {
            _pendingCommandLink = link;
            e.Handled = true;
            return;
        }
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            var selection = Terminal.Selection.HasSelection ? Terminal.Selection.GetSelectionText() : null;
            MenuRequested?.Invoke(LineAt(point.Position.Y), string.IsNullOrWhiteSpace(selection) ? null : selection);
            return;
        }
        base.OnPointerPressed(e);
    }

    /// <summary>Cmd+click opens links too; on by default on macOS only. Settable for tests.</summary>
    internal static bool CommandClickOpensLinks { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>A Cmd+click on a link (macOS). The library raises its own UrlClicked for Control+click.</summary>
    public event Action<string>? CommandLinkClicked;
    private string? _pendingCommandLink;
    private bool _hoverHint;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_pendingCommandLink is { } pending)
        {
            _pendingCommandLink = null;
            e.Handled = true;
            if (LinkAt(e.GetPosition(this)) == pending) CommandLinkClicked?.Invoke(pending);
            return;
        }
        base.OnPointerReleased(e);
    }

    /// <summary>
    /// The library draws a link under the pointer but reports hovering to no one, so the same lookup runs here to show
    /// how to open it: a tooltip that names Ctrl+click (and Cmd+click on macOS) while the pointer is over a link.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var over = LinkAt(e.GetPosition(this)) is not null;
        if (over == _hoverHint) return;
        _hoverHint = over;
        if (over) Bind(ToolTip.TipProperty, LocalizedText.Binding(CommandClickOpensLinks ? nameof(Wandur.Core.Localization.Strings.LinkClickHintMac) : nameof(Wandur.Core.Localization.Strings.LinkClickHint)));
        else { ClearValue(ToolTip.TipProperty); ToolTip.SetIsOpen(this, false); }
    }

    // The library's own detection: http or https up to whitespace or a quote, then trailing punctuation trimmed.
    private static readonly Regex UrlPattern = new("https?://[^\\s<>\"'`]+", RegexOptions.CultureInvariant);

    /// <summary>
    /// The link drawn at a point in this control: an OSC 8 link on that cell, else a web address found in the logical
    /// line (wrapped rows joined), the same way the library finds one. Columns follow the library's pointer mapping;
    /// a wide character earlier on the line can shift the match by a column, which only matters at a link's edge.
    /// </summary>
    internal string? LinkAt(Point point)
    {
        if (CharWidth <= 0 || CharHeight <= 0) return null;
        var buffer = Terminal.Buffer;
        var column = Math.Clamp((int)((point.X - Math.Max(0, GutterWidth)) / CharWidth), 0, Math.Max(0, Terminal.Cols - 1));
        var row = buffer.ViewportY + Math.Clamp((int)(point.Y / CharHeight), 0, Math.Max(0, Terminal.Rows - 1));
        if (row < 0 || row >= buffer.Lines.Length || buffer.GetLine(row) is not { } line) return null;
        if (line.HasLinks && line.TryGetLinkAt(column, out var hyperlink)) return hyperlink.Url;
        var start = row;
        while (start > 0 && buffer.GetLine(start)?.IsWrapped == true) start--;
        var text = new StringBuilder();
        var offset = -1;
        for (var i = start; i < buffer.Lines.Length; i++)
        {
            var part = buffer.GetLine(i);
            if (part is null || (i > start && !part.IsWrapped)) break;
            if (i == row) offset = text.Length + column;
            text.Append(part.TranslateToString(false, 0, Math.Min(part.Length, Terminal.Cols)).PadRight(Terminal.Cols));
            if (text.Length > 16_384) break;
        }
        if (offset < 0) return null;
        foreach (Match match in UrlPattern.Matches(text.ToString()))
        {
            var url = TrimUrlEnd(match.Value);
            if (offset >= match.Index && offset < match.Index + url.Length) return url;
        }
        return null;
    }

    private static string TrimUrlEnd(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            if (last is '!' or '"' or '\'' or ',' or '.' or ':' or ';' or '?') { url = url[..^1]; continue; }
            var open = last switch { ')' => '(', ']' => '[', '}' => '{', _ => '\0' };
            if (open == '\0' || url.Count(c => c == open) >= url.Count(c => c == last)) break;
            url = url[..^1];
        }
        return url;
    }

    /// <summary>The text of the logical line drawn at this height, with wrapped continuations joined.</summary>
    public string? LineAt(double y)
    {
        var height = CharHeight;
        if (height <= 0) return null;
        var buffer = Terminal.Buffer;
        var row = buffer.ViewportY + Math.Clamp((int)(y / height), 0, Math.Max(0, Terminal.Rows - 1));
        if (row < 0 || row >= buffer.Lines.Length) return null;
        var start = row;
        while (start > 0 && buffer.GetLine(start)?.IsWrapped == true) start--;
        var text = new System.Text.StringBuilder();
        for (var i = start; i < buffer.Lines.Length; i++)
        {
            var line = buffer.GetLine(i);
            if (line is null || (i > start && !line.IsWrapped)) break;
            text.Append(line.TranslateToString(true));
        }
        var result = text.ToString().TrimEnd();
        return result.Length == 0 ? null : result;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // A MUD's cursor-style command must not enable the library's focus-dependent blink clock.
        if (change.Property == CursorBlinkProperty && CursorBlink) SetCurrentValue(CursorBlinkProperty, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // A shorter viewport (including the output tabs) can add scrollback during reflow.
        // Keep following if we were at the tail; preserve a reader's older scroll position.
        var following = Terminal.Buffer.IsAtBottom;
        var result = base.ArrangeOverride(finalSize);
        if (following)
        {
            ViewportY = Terminal.Buffer.YBase;
            // The split view while scrolled back shortens the grid without the window changing, so only a
            // layout at the tail says what size the reader's terminal is.
            var grid = (Terminal.Cols, Terminal.Rows);
            if (grid != _reportedGrid) { _reportedGrid = grid; GridResized?.Invoke(); }
        }
        return result;
    }

    private (int Columns, int Rows) _reportedGrid;

    /// <summary>The grid from the last layout at the tail, or null before the first one.</summary>
    public (int Columns, int Rows)? ReportedGrid => _reportedGrid == default ? null : _reportedGrid;

    /// <summary>The character grid changed size in a layout pass at the tail.</summary>
    public event Action? GridResized;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _attached = true; UpdateTimer(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _attached = false; UpdateTimer(); base.OnDetachedFromVisualTree(e); }
    private void UpdateTimer()
    { if (_attached && _allowBlink) _blinkTimer.Start(); else { _blinkTimer.Stop(); _hiddenPhase = false; } }

    public override void Render(DrawingContext context)
    {
        if (!_allowBlink || !_hiddenPhase) { base.Render(context); return; }
        // No PTY is attached: writes and drawing both run on the UI thread. Temporarily conceal
        // visible blinking cells, then restore them so export, selection, and future writes retain
        // the server's original attributes. The public cell setter invalidates cached glyph runs.
        var changed = new List<(BufferLine Line, int Column, BufferCell Cell)>();
        try
        {
            for (int y = Terminal.Buffer.YDisp; y < Math.Min(Terminal.Buffer.Lines.Length, Terminal.Buffer.YDisp + Terminal.Rows); y++)
            {
                var line = Terminal.Buffer.GetLine(y);
                if (line is null) continue;
                for (var x = 0; x < line.Length; x++)
                {
                    var cell = line[x];
                    if (!cell.Attributes.IsBlink() || cell.Attributes.IsInvisible()) continue;
                    changed.Add((line, x, cell));
                    cell.Attributes.SetInvisible(true);
                    line[x] = cell;
                }
            }
            base.Render(context);
        }
        finally { foreach (var entry in changed) entry.Line[entry.Column] = entry.Cell; }
    }

    public void StopBlinking() => _blinkTimer.Stop();
}
