using System.Text;

namespace Wandur.Core.Terminal;

public sealed record TextStyle(string? Foreground = null, string? Background = null, bool Bold = false, bool Italic = false, bool Underline = false)
{
    public int? ForegroundIndex { get; init; }
    public int? BackgroundIndex { get; init; }
}
public sealed record TextRun(string Text, TextStyle Style);
public sealed record TerminalLine(IReadOnlyList<TextRun> Runs)
{
    public string Text => string.Concat(Runs.Select(r => r.Text));
    /// <summary>The length of <see cref="Text"/>, counted without building it.</summary>
    internal int Length
    {
        get
        {
            var length = 0;
            for (var i = 0; i < Runs.Count; i++) length += Runs[i].Text.Length;
            return length;
        }
    }
}

public sealed class AnsiTerminal
{
    private enum ParseState { Text, Escape, Csi, Osc, OscEscape, DiscardCsi }
    private readonly record struct Cell(char Character, TextStyle Style);
    /// <summary>The fields of a <see cref="TextStyle"/>, so a sequence that lands on a style already seen reuses it.</summary>
    private readonly record struct StyleKey(string? Foreground, string? Background, bool Bold, bool Italic, bool Underline, int? ForegroundIndex, int? BackgroundIndex);
    private static readonly TextStyle Plain = new();
    private static readonly TextStyle LocalEcho = new(Foreground: "#808080") { ForegroundIndex = 8 };
    private const int MaximumSequence = 128;
    /// <summary>Distinct styles remembered per parser. Truecolor output can name endless styles, so the cache starts over when full.</summary>
    private const int MaximumStyles = 256;
    // A queue, not a list: trimming the oldest line of a full scrollback must not shift the other 1,999 every time.
    private readonly Queue<TerminalLine> _completed = new();
    private readonly List<Cell> _current = [];
    private readonly int _maxLines;
    private readonly char[] _sequence = new char[MaximumSequence];
    private int _sequenceLength;
    // A semicolon separates parameters, so 128 characters hold at most 65 of them.
    private readonly int[] _values = new int[MaximumSequence / 2 + 1];
    private readonly StringBuilder _runText = new();
    private Dictionary<StyleKey, TextStyle>? _styles;
    private TextStyle _style = Plain;
    private int? _basicForegroundIndex;
    private ParseState _state;
    private int _cursor;
    private int _completedCharacters;
    private IReadOnlyList<TerminalLine>? _snapshot;

    public AnsiTerminal(int maxLines = 2000) => _maxLines = Math.Clamp(maxLines, 2, 10_000);
    public IReadOnlyList<TerminalLine> Lines => _snapshot ??= Snapshot();

    private TerminalLine[] Snapshot()
    {
        var lines = new TerminalLine[_completed.Count + 1];
        _completed.CopyTo(lines, 0);
        lines[^1] = CurrentLine();
        return lines;
    }
    public event Action<string, bool>? OutputAppended;
    public event Action? Cleared;
    /// <summary>A completed display line, before scrollback eviction. Subscribers must not mutate this parser.</summary>
    public event Action<TerminalLine>? LineCompleted;
    /// <summary>Sticky until newline/clear, even if cursor editing later shortens the line.</summary>
    public bool CurrentLineTruncated { get; private set; }
    /// <summary>The whole transcript as text. Builds every line, so it is for explicit requests (export, copy), not for checks.</summary>
    public string PlainText => string.Join('\n', Lines.Select(l => l.Text));
    /// <summary>Whether <see cref="PlainText"/> would be non-empty, without building it: a completed line (even an empty one
    /// adds a separator) or a character on the line in progress.</summary>
    public bool HasText => _completed.Count > 0 || _current.Count > 0;

    public void AppendLocalText(string text)
    {
        // Local text must not consume a pending server escape sequence or execute controls.
        _snapshot = null;
        var serverStyle = _style;
        _style = LocalEcho;
        foreach (var ch in text)
        {
            if (ch == '\n') NewLine();
            else if (!char.IsControl(ch)) Put(ch);
        }
        _style = serverStyle;
        OutputAppended?.Invoke(text, true);
    }

    public void Clear()
    {
        ClearState();
        Cleared?.Invoke();
    }

    private void ClearState()
    {
        _completed.Clear(); _current.Clear(); _sequenceLength = 0; _cursor = 0;
        _completedCharacters = 0; _style = Plain; _basicForegroundIndex = null; _state = ParseState.Text; _snapshot = null;
        CurrentLineTruncated = false;
    }

    public void Append(string text)
    {
        _snapshot = null;
        foreach (char ch in text)
        {
            switch (_state)
            {
                case ParseState.Text:
                    if (ch == '\u001b') _state = ParseState.Escape;
                    else if (ch == '\n') NewLine();
                    else if (ch == '\r') _cursor = 0;
                    else if (ch == '\b') _cursor = Math.Max(0, _cursor - 1);
                    else if (ch == '\t') { int count = 4 - _cursor % 4; for (int n = 0; n < count; n++) Put(' '); }
                    else if (!char.IsControl(ch)) Put(ch);
                    break;
                case ParseState.Escape:
                    if (ch == '[') { _sequenceLength = 0; _state = ParseState.Csi; }
                    else if (ch is ']' or 'P' or '^' or '_') _state = ParseState.Osc;
                    else _state = ParseState.Text;
                    break;
                case ParseState.Csi:
                    if (ch is >= '@' and <= '~') { ApplyCsi(ch); _state = ParseState.Text; }
                    else if (_sequenceLength < MaximumSequence) _sequence[_sequenceLength++] = ch;
                    else _state = ParseState.DiscardCsi;
                    break;
                case ParseState.DiscardCsi:
                    if (ch is >= '@' and <= '~') _state = ParseState.Text;
                    break;
                case ParseState.Osc:
                    if (ch == '\u0007') _state = ParseState.Text;
                    else if (ch == '\u001b') _state = ParseState.OscEscape;
                    break;
                case ParseState.OscEscape:
                    _state = ch == '\\' ? ParseState.Text : ParseState.Osc;
                    break;
            }
        }
        OutputAppended?.Invoke(text, false);
    }

    private void Put(char ch)
    {
        if (_cursor >= 4096) { CurrentLineTruncated = true; return; }
        var cell = new Cell(ch, _style);
        if (_cursor < _current.Count) _current[_cursor] = cell;
        else _current.Add(cell);
        _cursor++;
    }

    private void NewLine()
    {
        var line = CurrentLine();
        LineCompleted?.Invoke(line);
        _completed.Enqueue(line);
        _completedCharacters += _current.Count;
        _current.Clear(); _cursor = 0;
        CurrentLineTruncated = false;
        while (_completed.Count >= _maxLines || _completedCharacters > 200_000)
            _completedCharacters -= _completed.Dequeue().Length;
    }

    private TerminalLine CurrentLine()
    {
        var runs = new List<TextRun>();
        var text = _runText;
        text.Clear();
        TextStyle? style = null;
        foreach (var cell in _current)
        {
            // The same instance is the same style; only a different instance needs the field by field comparison.
            if (style is not null && !ReferenceEquals(style, cell.Style) && style != cell.Style)
            {
                runs.Add(new(text.ToString(), style)); text.Clear();
            }
            style = cell.Style; text.Append(cell.Character);
        }
        if (style is not null) runs.Add(new(text.ToString(), style));
        text.Clear();
        return new(runs);
    }

    /// <summary>
    /// Splits the parameters as <c>string.Split(';')</c> followed by <c>int.TryParse</c> (0 when that fails) would, into
    /// a reused buffer: the same numbers, without a string, an array and a substring per escape sequence.
    /// </summary>
    private int ParseValues()
    {
        var count = 0;
        var sequence = _sequence.AsSpan(0, _sequenceLength);
        while (true)
        {
            var separator = sequence.IndexOf(';');
            var part = separator < 0 ? sequence : sequence[..separator];
            _values[count++] = int.TryParse(part, out var n) ? n : 0;
            if (separator < 0) return count;
            sequence = sequence[(separator + 1)..];
        }
    }

    private void ApplyCsi(char command)
    {
        var count = ParseValues();
        var values = _values;
        if (command == 'K')
        {
            if (values[0] == 0 && _cursor < _current.Count) _current.RemoveRange(_cursor, _current.Count - _cursor);
            else if (values[0] == 2) { _current.Clear(); _cursor = 0; }
        }
        else if (command == 'J' && values[0] is 2 or 3) ClearState();
        else if (command == 'm')
        {
            var s = Key(_style);
            for (var i = 0; i < count; i++)
            {
                var n = values[i];
                if (n is >= 30 and <= 37) _basicForegroundIndex = n - 30;
                else if (n is 0 or 39 or >= 90 and <= 97) _basicForegroundIndex = null;
                s = n switch
                {
                    0 => default, 1 => s with { Bold = true }, 22 => s with { Bold = false },
                    3 => s with { Italic = true }, 23 => s with { Italic = false },
                    4 => s with { Underline = true }, 24 => s with { Underline = false },
                    39 => s with { Foreground = null, ForegroundIndex = null }, 49 => s with { Background = null, BackgroundIndex = null },
                    >= 30 and <= 37 => s with { Foreground = IndexedColor(n - 30), ForegroundIndex = n - 30 },
                    >= 90 and <= 97 => s with { Foreground = IndexedColor(n - 90 + 8), ForegroundIndex = n - 90 + 8 },
                    >= 40 and <= 47 => s with { Background = IndexedColor(n - 40), BackgroundIndex = n - 40 },
                    >= 100 and <= 107 => s with { Background = IndexedColor(n - 100 + 8), BackgroundIndex = n - 100 + 8 },
                    _ => s
                };
                if (n is not (38 or 48) || i + 1 >= count) continue;
                string? color = null;
                int? paletteIndex = null;
                if (values[i + 1] == 5 && i + 2 < count)
                { var index = Math.Clamp(values[i + 2], 0, 255); color = IndexedColor(index); paletteIndex = index < 16 ? index : null; i += 2; }
                else if (values[i + 1] == 2 && i + 4 < count)
                { color = $"#{Math.Clamp(values[i + 2], 0, 255):X2}{Math.Clamp(values[i + 3], 0, 255):X2}{Math.Clamp(values[i + 4], 0, 255):X2}"; i += 4; }
                if (color is not null)
                {
                    if (n == 38) { _basicForegroundIndex = null; s = s with { Foreground = color, ForegroundIndex = paletteIndex }; }
                    else s = s with { Background = color, BackgroundIndex = paletteIndex };
                }
            }
            // Legacy MUDs use SGR 1 + colors 30–37 for the bright palette. Resolve
            // after the whole sequence so 1;30, 30;1 and separate sequences agree.
            if (_basicForegroundIndex is { } basic)
                s = s with { Foreground = IndexedColor(basic + (s.Bold ? 8 : 0)), ForegroundIndex = basic + (s.Bold ? 8 : 0) };
            _style = Style(s);
        }
    }

    private static StyleKey Key(TextStyle style) =>
        new(style.Foreground, style.Background, style.Bold, style.Italic, style.Underline, style.ForegroundIndex, style.BackgroundIndex);

    /// <summary>The style these fields describe, shared with every earlier cell that had it: styles are immutable, and
    /// sharing them keeps a busy transcript from holding a fresh copy per escape sequence.</summary>
    private TextStyle Style(StyleKey key)
    {
        if (key == Key(_style)) return _style;
        if (key == default) return Plain;
        _styles ??= [];
        if (_styles.TryGetValue(key, out var known)) return known;
        if (_styles.Count >= MaximumStyles) _styles.Clear();
        var style = new TextStyle(key.Foreground, key.Background, key.Bold, key.Italic, key.Underline)
            { ForegroundIndex = key.ForegroundIndex, BackgroundIndex = key.BackgroundIndex };
        _styles[key] = style;
        return style;
    }

    private static string IndexedColor(int n) => AnsiPalette.Indexed(n);
}
