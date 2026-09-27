using Avalonia.Threading;
using Wandur.Core.Sessions;

namespace Wandur.Desktop;

/// <summary>
/// Telnet features that reach past the transcript: the window size the server is told (NAWS).
/// </summary>
public sealed partial class WorkspaceController
{
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
