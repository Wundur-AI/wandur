using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wandur.Core.Sessions;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

/// <summary>The server is told the transcript's real character grid through NAWS, and told again after a resize.</summary>
public sealed class WindowSizeTests
{
    private static WorkspaceController Controller() => new(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(),
        new SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-naws-" + Guid.NewGuid(), "settings.json")),
        new MemoryPasswordVault(), new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());

    private static byte[] WindowSize(int columns, int rows) =>
        [255, 250, 31, (byte)(columns >> 8), (byte)columns, (byte)(rows >> 8), (byte)rows, 255, 240];

    private static (int Columns, int Rows) Clamped((int Columns, int Rows) size) =>
        (Math.Clamp(size.Columns, TelnetSession.MinimumColumns, TelnetSession.MaximumWindowSize),
         Math.Clamp(size.Rows, TelnetSession.MinimumRows, TelnetSession.MaximumWindowSize));

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            // The UI thread must keep running for the debounce timer while the test waits for bytes.
            var pending = stream.ReadAsync(buffer.AsMemory(read), token).AsTask();
            while (!pending.IsCompleted) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, token); }
            var n = await pending;
            Assert.True(n > 0);
            read += n;
        }
        return buffer;
    }

    [AvaloniaFact]
    public async Task TheTranscriptGridIsSentOnDoNawsAndAfterAResize()
    {
        await using var controller = Controller();
        var window = new Window { Width = 900, Height = 600, Content = controller.Display.View };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var profile = new ConnectionProfile { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await controller.StartAsync(profile);
        using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
        var stream = socket.GetStream();

        var initial = Clamped(controller.Display.TerminalSize);
        Assert.NotEqual((100, 40), initial);
        await stream.WriteAsync(new byte[] { 255, 253, 31 }, timeout.Token);
        byte[] expected = [255, 251, 31, .. WindowSize(initial.Columns, initial.Rows)];
        Assert.Equal(expected, await ReadAsync(stream, expected.Length, timeout.Token));

        window.Width = 600; window.Height = 400;
        Dispatcher.UIThread.RunJobs();
        var resized = Clamped(controller.Display.TerminalSize);
        Assert.NotEqual(initial, resized);
        var update = WindowSize(resized.Columns, resized.Rows);
        Assert.Equal(update, await ReadAsync(stream, update.Length, timeout.Token));
        window.Close();
    }
}
