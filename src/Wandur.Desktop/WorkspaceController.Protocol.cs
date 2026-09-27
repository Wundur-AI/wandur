using Avalonia.Threading;
using Wandur.Core.Scripting;
using Wandur.Core.Sessions;

namespace Wandur.Desktop;

/// <summary>
/// Telnet features that reach past the transcript: the window size the server is told (NAWS), the
/// prompts servers mark with GA or EOR, and the server's own description (MSSP).
/// </summary>
public sealed partial class WorkspaceController
{
    /// <summary>The script event kind for a prompt, <c>Events.Prompt</c> in scripts.</summary>
    internal const string ScriptPromptKind = "prompt";

    /// <summary>
    /// A prompt the server marked with GA or EOR, as plain text, raised on the UI thread after the text before it
    /// reached the transcript. Only public prompts: nothing while input is private, during auto-login, or from a
    /// stretch the server or the privacy stamp marked private. Scripts receive the same prompts as Events.Prompt.
    /// </summary>
    public event Action<string>? PromptReceived;

    private void ReceivePrompt(IMudSession session, ReceivedSessionText prompt, bool wirePrivate)
    {
        var text = prompt.Text.Length > 4096 ? prompt.Text[..4096] : prompt.Text;
        lock (_pendingLock)
        {
            if (_pendingCharacters + text.Length > 524_288 || _pending.Count >= 2048)
            { _pending.Clear(); _pendingCharacters = 0; _droppedOutput = true; }
            var epoch = prompt.MayContainPrivateText || wirePrivate || _scriptPrivacyBlocked ? -1 : _scriptOutputEpoch;
            _pending.Enqueue((session, "", epoch, new(ScriptPromptKind, text), null, -1));
            _pendingCharacters += text.Length;
        }
    }

    /// <summary>The server's MSSP self-description for this session, or null when it sent none.</summary>
    public Wandur.Core.Protocol.MsspTable? ServerDetails { get; private set; }

    /// <summary>
    /// An MSSP table, on the receive thread: formatted and masked now, while the remembered secrets are those of this
    /// moment, and queued with the other protocol diagnostics so it lands in wire order. A table from a read that may
    /// contain private text (an echo-off stretch) is skipped entirely: not listed, not shown, not used.
    /// </summary>
    private void ReceiveMssp(IMudSession session, Wandur.Core.Protocol.MsspTable table, bool mayContainPrivateText, bool wirePrivate)
    {
        if (mayContainPrivateText || wirePrivate) return;
        var (content, details) = ViewModels.ProtocolDiagnosticsViewModel.FormatServerDetails(table, _diagnosticSecrets);
        lock (_pendingLock)
        {
            var diagnostic = new PendingProtocolDiagnostic(DateTimeOffset.UtcNow, 70, null, content) { Mssp = table, ServerDetails = details };
            var size = DiagnosticSize(diagnostic);
            while (_pendingDiagnostics.Count > 0 && (_pendingDiagnosticBytes + size > 524_288 || _pendingDiagnostics.Count >= 200))
                _pendingDiagnosticBytes -= DiagnosticSize(_pendingDiagnostics.Dequeue().Message);
            _pendingDiagnostics.Enqueue((session, -1, -1, diagnostic));
            _pendingDiagnosticBytes += size;
        }
    }

    /// <summary>
    /// Flushed in wire order: the table is kept for the session, shown in the Server details tab, and its CODEBASE
    /// picks the channel family when the profile names none. Nothing is written to the profile.
    /// </summary>
    private void ApplyServerDetails(Wandur.Core.Protocol.MsspTable table, string details)
    {
        ServerDetails = table;
        Diagnostics.SetServerDetails(table, details);
        UseServerCodebase(table.GetFirst("CODEBASE"));
    }

    /// <summary>How long the terminal must keep one size before the server hears about it, so dragging a
    /// splitter or the window edge sends one update instead of dozens.</summary>
    internal static readonly TimeSpan WindowSizeDebounce = TimeSpan.FromMilliseconds(250);
    private DispatcherTimer? _windowSizeTimer;

    private void InitializeWindowSize()
    {
        _windowSizeTimer = new DispatcherTimer { Interval = WindowSizeDebounce };
        _windowSizeTimer.Tick += (_, _) => { _windowSizeTimer.Stop(); SendWindowSize(); };
        Display.TerminalSizeChanged += ScheduleWindowSize;
    }

    private void ScheduleWindowSize()
    {
        if (_disposed || _windowSizeTimer is null) return;
        _windowSizeTimer.Stop();
        _windowSizeTimer.Start();
    }

    private void StopWindowSize()
    {
        _windowSizeTimer?.Stop();
        Display.TerminalSizeChanged -= ScheduleWindowSize;
    }

    /// <summary>Tells the session the terminal's current grid. Before connecting this only records it for the
    /// NAWS reply; afterwards a change is sent once the server has agreed to NAWS.</summary>
    private void SendWindowSize()
    {
        if (_session is not TelnetSession telnet) return;
        var (columns, rows) = Display.TerminalSize;
        _ = SendWindowSizeAsync(telnet, columns, rows);
    }

    private static async Task SendWindowSizeAsync(TelnetSession telnet, int columns, int rows)
    {
        try { await telnet.UpdateWindowSizeAsync(columns, rows); }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        { /* The connection is ending; the next one sends the size in its own negotiation. */ }
    }
}
