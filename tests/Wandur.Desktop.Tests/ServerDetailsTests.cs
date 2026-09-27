using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Channels;
using Wandur.Core.Settings;
using Wandur.Desktop.Views;

namespace Wandur.Desktop.Tests;

/// <summary>A server's MSSP description reaches diagnostics and, when the profile names no codebase, the channel family.</summary>
public sealed class ServerDetailsTests
{
    private static byte[] Mssp(params (string Name, string Value)[] pairs)
    {
        var bytes = new List<byte> { 255, 250, 70 };
        foreach (var (name, value) in pairs)
        {
            bytes.Add(1); bytes.AddRange(Encoding.UTF8.GetBytes(name));
            bytes.Add(2); bytes.AddRange(Encoding.UTF8.GetBytes(value));
        }
        bytes.AddRange([255, 240]);
        return [.. bytes];
    }

    private static async Task<(WorkspaceController Controller, TcpClient Server, TcpListener Listener, string Path)> OpenAsync(string codebase)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-mssp-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        var controller = new WorkspaceController(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var profile = new ConnectionProfile { Name = "Fixture", Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, Codebase = codebase };
        await controller.SaveWorldAsync(profile, "", false);
        profile = Assert.Single(controller.Settings.Profiles);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await controller.StartAsync(profile);
        var server = await listener.AcceptTcpClientAsync(timeout.Token);
        return (controller, server, listener, path);
    }

    private static async Task WaitFor(WorkspaceController controller, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the server details.");
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            controller.FlushOutput();
        }
    }

    [AvaloniaFact]
    public async Task AnMsspTableReachesDiagnosticsAndPicksTheChannelFamilyWithoutSavingIt()
    {
        var (controller, server, listener, _) = await OpenAsync("");
        await using var _c = controller;
        using var _s = server; using var _l = listener.Server;
        Assert.Same(ChannelFamilies.Family(ChannelFamilies.Generic), controller.ChannelRules);
        Assert.False(controller.Diagnostics.HasServerDetails);
        server.GetStream().Write([255, 251, 70, .. Mssp(("NAME", "Fixture World"), ("CODEBASE", "SmaugFUSS 1.9"), ("PLAYERS", "12"))]);
        await WaitFor(controller, () => controller.ServerDetails is not null);

        Assert.Equal("Fixture World", controller.ServerDetails!.GetFirst("NAME"));
        Assert.True(controller.Diagnostics.HasServerDetails);
        Assert.Equal("NAME: Fixture World\nCODEBASE: SmaugFUSS 1.9\nPLAYERS: 12", controller.Diagnostics.ServerDetails);
        var entry = Assert.Single(controller.Diagnostics.Entries, e => e.Protocol == "MSSP");
        Assert.Contains("\"CODEBASE\": \"SmaugFUSS 1.9\"", entry.Content!.Body);
        Assert.Contains("MSSP", controller.Diagnostics.Kinds.Select(k => k.Name));

        Assert.Equal("SmaugFUSS 1.9", controller.ChannelCodebase);
        Assert.Same(ChannelFamilies.Family("smaug"), controller.ChannelRules);
        Assert.Equal("", Assert.Single(controller.Settings.Profiles).Codebase);
        Assert.Equal("", controller.ActiveProfile!.Codebase);

        // Shown in the diagnostics view's Server details tab.
        var view = new ProtocolDiagnosticsView(controller.Diagnostics) { Width = 900, Height = 600 };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var tab = view.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "ProtocolDiagnosticTabs");
        tab.SelectedItem = tab.Items.OfType<TabItem>().Single(t => t.Name == "ProtocolServerTab");
        Dispatcher.UIThread.RunJobs();
        var detail = view.GetVisualDescendants().OfType<DiagnosticsBodyEditor>().Single(e => e.Name == "ProtocolServerDetail");
        Assert.Contains("CODEBASE: SmaugFUSS 1.9", detail.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheProfileCodebaseWinsOverTheReportedOne()
    {
        var (controller, server, listener, _) = await OpenAsync("ROM 2.4");
        await using var _c = controller;
        using var _s = server; using var _l = listener.Server;
        server.GetStream().Write([255, 251, 70, .. Mssp(("NAME", "Fixture"), ("CODEBASE", "SmaugFUSS"))]);
        await WaitFor(controller, () => controller.ServerDetails is not null);
        Assert.Equal("ROM 2.4", controller.ChannelCodebase);
        Assert.Same(ChannelFamilies.Family("rom"), controller.ChannelRules);
    }
}
