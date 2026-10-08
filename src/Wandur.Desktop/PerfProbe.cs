using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;

namespace Wandur.Desktop;

/// <summary>
/// Opt-in measurement for the performance harness (bench/Wandur.Bench, docs/perf.md). Off unless the
/// WANDUR_PERF_PROBE environment variable names an output file; then it records when the main window first became
/// usable, optionally drives one scenario (loopback sessions or the directory), samples memory, CPU and UI thread
/// latency as JSON lines, and closes the app. Nothing here runs, allocates or subscribes when the variable is unset.
/// </summary>
internal static class PerfProbe
{
    private static string? _output;
    private static readonly Lock Gate = new();
    private static readonly List<double> Latencies = [];
    private static long _appendedCharacters;

    internal static bool Enabled => _output is not null;

    /// <summary>Called once from <see cref="App"/> after the main window exists.</summary>
    internal static void Attach(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _output = Environment.GetEnvironmentVariable("WANDUR_PERF_PROBE");
        if (string.IsNullOrWhiteSpace(_output)) { _output = null; return; }
        window.Opened += (_, _) => window.RequestAnimationFrame(frame =>
            Dispatcher.UIThread.Post(() => _ = RunAsync(window, desktop), DispatcherPriority.Background));
    }

    private static async Task RunAsync(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var process = Process.GetCurrentProcess();
        var usable = (DateTime.Now - process.StartTime).TotalMilliseconds;
        Write(new { phase = "usable", startupMs = usable, assemblies = Assemblies() });
        var scenario = Environment.GetEnvironmentVariable("WANDUR_PERF_SCENARIO") ?? "idle";
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("WANDUR_PERF_SECONDS"), out var s) ? s : 5;
        var settle = int.TryParse(Environment.GetEnvironmentVariable("WANDUR_PERF_SETTLE"), out var w) ? w : 3;
        using var ticker = StartLatencyTicker();
        try
        {
            if (scenario == "sessions")
            {
                var target = Environment.GetEnvironmentVariable("WANDUR_PERF_HOST") ?? "127.0.0.1:4400";
                var count = int.TryParse(Environment.GetEnvironmentVariable("WANDUR_PERF_SESSIONS"), out var n) ? n : 1;
                var separator = target.LastIndexOf(':');
                var host = target[..separator];
                var port = int.Parse(target[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                for (var i = 0; i < count; i++)
                {
                    var profile = new ConnectionProfile { Name = $"Bench {i + 1}", Host = host, Port = port };
                    await window.Sessions.OpenAsync(profile);
                }
                // Earlier tabs are now inactive; only the last one opened is shown.
                foreach (var tab in window.Sessions.Tabs)
                    tab.Controller.Terminal.OutputAppended += (text, local) => { if (!local) Interlocked.Add(ref _appendedCharacters, text.Length); };
                Write(new { phase = "opened", sessions = window.Sessions.Tabs.Count(t => t.Controller.IsConnected) });
            }
            else if (scenario == "directory")
            {
                window.Workspace.ShowSearch();
                var waited = Stopwatch.StartNew();
                while (window.Catalog.Worlds.Count == 0 && waited.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(100);
                Write(new { phase = "catalog", worlds = window.Catalog.Worlds.Count, ms = waited.Elapsed.TotalMilliseconds });
            }
            await Task.Delay(TimeSpan.FromSeconds(settle));
            if (scenario == "directory") await ScrollDirectoryAsync(window);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Reset();
            var started = Sample(process, scenario, "start");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            Sample(process, scenario, "end", started);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Sample(process, scenario, "after-gc");
            Write(new { phase = "assemblies", assemblies = Assemblies() });
        }
        catch (Exception ex) { Write(new { phase = "error", error = ex.ToString() }); }
        Write(new { phase = "done", pid = Environment.ProcessId });
        // The harness reads the process footprint now, then creates the exit file.
        var exit = _output + ".exit";
        var wait = Stopwatch.StartNew();
        while (!File.Exists(exit) && wait.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(100);
        desktop.Shutdown();
    }

    private sealed record Snapshot(long Cpu, long Allocated, long Characters, long Ticks);

    private static Snapshot Sample(Process process, string scenario, string phase, Snapshot? since = null)
    {
        process.Refresh();
        var now = new Snapshot((long)process.TotalProcessorTime.TotalMilliseconds, GC.GetTotalAllocatedBytes(true),
            Interlocked.Read(ref _appendedCharacters), Stopwatch.GetTimestamp());
        double[] latencies;
        lock (Gate) { latencies = [.. Latencies]; Latencies.Clear(); }
        Array.Sort(latencies);
        double Percentile(double p) => latencies.Length == 0 ? 0 : latencies[Math.Min(latencies.Length - 1, (int)Math.Ceiling(p * latencies.Length) - 1)];
        var seconds = since is null ? 0 : Stopwatch.GetElapsedTime(since.Ticks, now.Ticks).TotalSeconds;
        var info = GC.GetGCMemoryInfo();
        Write(new
        {
            phase, scenario,
            workingSetMb = process.WorkingSet64 / 1048576.0,
            gcHeapMb = GC.GetTotalMemory(false) / 1048576.0,
            gcCommittedMb = info.TotalCommittedBytes / 1048576.0,
            cpuPercent = since is null ? 0 : (now.Cpu - since.Cpu) / 10.0 / seconds,
            allocMbPerSec = since is null ? 0 : (now.Allocated - since.Allocated) / 1048576.0 / seconds,
            charsPerSec = since is null ? 0 : (now.Characters - since.Characters) / seconds,
            uiP50 = Percentile(.5), uiP95 = Percentile(.95), uiMax = latencies.Length == 0 ? 0 : latencies[^1], uiSamples = latencies.Length,
            gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
            realizedRows = RealizedRows(),
            threads = process.Threads.Count
        });
        return now;
    }

    private static void Reset() { lock (Gate) Latencies.Clear(); }

    /// <summary>Every 50 ms, how long a job posted at input priority waits for the UI thread.</summary>
    private static IDisposable StartLatencyTicker()
    {
        var timer = new Timer(_ =>
        {
            var posted = Stopwatch.GetTimestamp();
            Dispatcher.UIThread.Post(() => { var ms = Stopwatch.GetElapsedTime(posted).TotalMilliseconds; lock (Gate) Latencies.Add(ms); }, DispatcherPriority.Input);
        }, null, 0, 50);
        return timer;
    }

    private static int RealizedRows() => Application() is { } window
        ? window.GetVisualDescendants().OfType<Views.DirectoryWorldCard>().Count() : 0;

    private static Window? Application() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>Scrolls the directory results from top to bottom and back, a page at a time, recording the most cards alive.</summary>
    private static async Task ScrollDirectoryAsync(MainWindow window)
    {
        var list = window.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(l => l.Name == "DirectoryResults");
        var scroll = list?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null) { Write(new { phase = "scroll", error = "no results list" }); return; }
        var most = RealizedRows();
        var steps = 0;
        var watch = Stopwatch.StartNew();
        while (scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 1 && steps < 500)
        {
            scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + scroll.Viewport.Height);
            await Task.Delay(30);
            most = Math.Max(most, RealizedRows());
            steps++;
        }
        var down = watch.Elapsed.TotalMilliseconds;
        scroll.Offset = scroll.Offset.WithY(0);
        await Task.Delay(200);
        Write(new { phase = "scroll", pages = steps, mostRealized = most, extent = scroll.Extent.Height, viewport = scroll.Viewport.Height, downMs = down, realizedAtTop = RealizedRows() });
    }

    private static string[] Assemblies() =>
        [.. AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "?").Order(StringComparer.Ordinal)];

    private static void Write(object value)
    {
        if (_output is null) return;
        var line = JsonSerializer.Serialize(value);
        lock (Gate) File.AppendAllText(_output, line + "\n");
    }
}
