using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

/// <summary>Prompts the server marks with GA or EOR reach the controller and scripts once each, and only when public.</summary>
public sealed class PromptEventTests
{
    private static async Task<(WorkspaceController Controller, RecordingScriptFactory Factory, TcpClient Server, List<string> Raised, TcpListener Listener)> OpenAsync()
    {
        var factory = new RecordingScriptFactory();
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-prompt-" + Guid.NewGuid(), "settings.json"));
        var controller = new WorkspaceController(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store, new MemoryPasswordVault(), new MemoryRoomMapStore(), factory, new MemoryScriptLibraryStore());
        var raised = new List<string>();
        controller.PromptReceived += raised.Add;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.StartAsync(new ConnectionProfile { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port });
        var server = await listener.AcceptTcpClientAsync(timeout.Token);
        await controller.ScriptLibrary.Items[0].Runtime.RunAsync();
        return (controller, factory, server, raised, listener);
    }

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    private static async Task WaitFor(WorkspaceController controller, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out. Private={controller.IsPrivate} Text=[{controller.Terminal.PlainText.Replace("\n", "|")}]");
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            controller.FlushOutput();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EachMarkedPromptReachesScriptsAndTheControllerOnce(bool endOfRecord)
    {
        var (controller, factory, server, raised, listener) = await OpenAsync();
        await using var _ = controller;
        using var __ = server; using var ___ = listener.Server;
        byte[] mark = endOfRecord ? [255, 239] : [255, 249];
        var stream = server.GetStream();
        if (endOfRecord) { stream.Write([255, 251, 25]); }
        stream.Write(Text("You arrive.\r\nHP: "));
        stream.Write([.. Text("12> "), .. mark, .. mark]);
        stream.Write(Text("\r\nbarrier\r\n"));
        await WaitFor(controller, () => factory.Runtime!.Events.Any(e => e.Text == "barrier"));
        var events = factory.Runtime!.Events.Where(e => e.Kind is "line" or "prompt").Select(e => e.Kind + ":" + e.Text).ToArray();
        Assert.Equal(["line:You arrive.", "prompt:HP: 12> ", "line:HP: 12> ", "line:barrier"], events);
        Assert.Equal(["HP: 12> "], raised);
    }

    [AvaloniaFact]
    public async Task PromptsWhilePrivateReachNeitherScriptsNorTheController()
    {
        var (controller, factory, server, raised, listener) = await OpenAsync();
        await using var _ = controller;
        using var __ = server; using var ___ = listener.Server;
        var stream = server.GetStream();
        // The server turns echo off for a password prompt.
        stream.Write([255, 251, 1, .. Text("Password: "), 255, 249]);
        await WaitFor(controller, () => controller.IsPrivate);
        // Echo back on; the password prompt is still the line in view, so input stays private until the next one.
        stream.Write([255, 252, 1, .. Text("\r\n")]);
        await WaitFor(controller, () => controller.Terminal.PlainText.EndsWith("Password: ") || controller.Terminal.PlainText.Contains("Password: \n"));
        // The reader's own Private toggle.
        controller.SetManualPrivate(true);
        stream.Write([.. Text("Secret code: "), 255, 249]);
        await Task.Delay(100);
        await WaitFor(controller, () => controller.Terminal.PlainText.Contains("Secret code"));
        controller.SetManualPrivate(false);
        // A prompt that matches the world's password prompt is private by its own text.
        stream.Write([.. Text("\r\nEnter your password: "), 255, 249]);
        await WaitFor(controller, () => controller.Terminal.PlainText.Contains("Enter your password"));
        Assert.True(controller.IsPrivate);
        // The next prompt is public again: it replaces the password prompt as the line in view.
        // Text that arrives while input is still private is withheld even when it ends the private stretch, as
        // lines are, so the first public prompt is the one after it.
        stream.Write([.. Text("\r\nHP: 3> "), 255, 249]);
        await WaitFor(controller, () => !controller.IsPrivate);
        stream.Write([.. Text("\r\nHP: 4> "), 255, 249]);
        await WaitFor(controller, () => factory.Runtime!.Events.Any(e => e.Kind == "prompt"));
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs(); controller.FlushOutput();
        Assert.Equal(["HP: 4> "], factory.Runtime!.Events.Where(e => e.Kind == "prompt").Select(e => e.Text));
        Assert.Equal(["HP: 4> "], raised);
    }
}
