using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Wandur.Core.Discovery;
using Wandur.Core.Mapping;
using Wandur.Core.Scripting;
using Wandur.Core.Settings;
using Wandur.Desktop;
using Wandur.Desktop.Security;

namespace Wandur.Bench;

/// <summary>Avalonia's headless platform with Skia, configured as the Desktop tests configure it.</summary>
public static class BenchApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class BenchMaps : IRoomMapStore
{
    private readonly Dictionary<(string, int), MapSnapshot> _maps = [];
    public MapSnapshot? Load(string host, int port) => _maps.GetValueOrDefault((host, port));
    public void Save(string host, int port, MapSnapshot snapshot) => _maps[(host, port)] = snapshot;
}

internal sealed class BenchVault : IPasswordVault
{
    public string Description => "bench";
    public Task<string?> ReadAsync(string key) => Task.FromResult<string?>(null);
    public Task WriteAsync(string key, string password) => Task.CompletedTask;
    public Task DeleteAsync(string key) => Task.CompletedTask;
}

internal sealed class BenchScripts : IWorldScriptLibraryStore
{
    public IReadOnlyList<WorldScriptDefinition> Load(string worldKey) => [];
    public void Upsert(string worldKey, WorldScriptDefinition script) { }
    public void Delete(string worldKey, Guid id) { }
}

public static class Harness
{
    internal static MainWindow Window(string settingsPath, WorldCatalog? catalog = null)
    {
        var window = new MainWindow(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), new SettingsStore(settingsPath), new BenchVault(), new BenchMaps(),
            new ProcessScriptRuntimeFactory(Environment.ProcessPath ?? "dotnet"), new BenchScripts(), catalog: catalog);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// Several sessions in one headless window, each fed by the loopback server at a fixed rate; the last opened is
    /// the active tab, the others are inactive. Reports process CPU time and managed allocation per MB of output the
    /// sessions' transcripts took in, which is independent of the rate the server paces at.
    /// </summary>
    public static async Task<Measurement> SessionsAsync(int sessions, int bytesPerSecond, int seconds, bool chat = true)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wandur-bench-sessions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await using var server = new MudServer(0, bytesPerSecond, chat: chat);
        var window = Window(Path.Combine(folder, "settings.json"), new WorldCatalog(Path.Combine(folder, "directory.json"), new Uri("http://127.0.0.1:9/")));
        try
        {
            for (var i = 0; i < sessions; i++)
                await window.Sessions.OpenAsync(new ConnectionProfile { Name = $"Bench {i + 1}", Host = "127.0.0.1", Port = server.Port });
            long characters = 0;
            foreach (var tab in window.Sessions.Tabs)
                tab.Controller.Terminal.OutputAppended += (text, local) => { if (!local) characters += text.Length; };
            await Wait(TimeSpan.FromSeconds(2));
            var process = Process.GetCurrentProcess();
            process.Refresh();
            var cpu = process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes(true);
            var before = characters;
            var clock = Stopwatch.StartNew();
            await Wait(TimeSpan.FromSeconds(seconds));
            process.Refresh();
            var mb = (characters - before) / 1048576.0;
            var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            var allocMb = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0;
            var connected = window.Sessions.Tabs.Count(t => t.Controller.IsConnected);
            return new($"{sessions} session(s) at {bytesPerSecond / 1000} KB/s each{(chat ? "" : ", no channel lines")}, headless window", "MB of output",
                cpuMs / mb, allocMb * 1024 / mb,
                $"connected {connected}, {mb / clock.Elapsed.TotalSeconds:F2} MB/s taken in, CPU {cpuMs / clock.Elapsed.TotalMilliseconds * 100:F0}% of one core, {allocMb / clock.Elapsed.TotalSeconds:F1} MB/s allocated");
        }
        finally
        {
            window.Close();
            await Wait(TimeSpan.FromSeconds(1));
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    /// <summary>
    /// Switching the active session: two sessions that each took in about 300 KB of chatty output (so the Channels panel
    /// has a full history), then the active tab alternates between them. Each switch runs the dispatcher's queued work and
    /// a layout pass of the window, which is what the user waits for before the panels show the other world.
    /// </summary>
    public static async Task<Measurement> SwitchAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wandur-bench-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await using var server = new MudServer(0, 400_000, limitBytes: 300_000, chat: true);
        var window = Window(Path.Combine(folder, "settings.json"), new WorldCatalog(Path.Combine(folder, "directory.json"), new Uri("http://127.0.0.1:9/")));
        try
        {
            window.Width = 1440; window.Height = 900;
            for (var i = 0; i < 2; i++)
                await window.Sessions.OpenAsync(new ConnectionProfile { Name = $"Bench {i + 1}", Host = "127.0.0.1", Port = server.Port });
            await Wait(TimeSpan.FromSeconds(3));
            var tabs = window.Sessions.Tabs.ToArray();
            var n = 0;
            return Micro.Time("Switch the active session (2 sessions, channel history, 1440 by 900)", "switch", 9, 10, () =>
            {
                window.Sessions.Select(tabs[n++ % tabs.Length]);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }, $"{server.BytesSent / 1024} KB sent");
        }
        finally
        {
            window.Close();
            await Wait(TimeSpan.FromSeconds(1));
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    /// <summary>Lets the headless dispatcher run its timers (the 60 ms output flush) for a while.</summary>
    internal static async Task Wait(TimeSpan time)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < time)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
    }
}
