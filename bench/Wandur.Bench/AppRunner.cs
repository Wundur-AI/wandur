using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace Wandur.Bench;

/// <summary>
/// Launches the built client (Release) with its probe on (WANDUR_PERF_PROBE), a throwaway data folder under
/// .superpowers/perf, and only loopback servers, then reads the probe's samples and the macOS physical footprint.
/// </summary>
public static partial class AppRunner
{
    public sealed record Run(string Scenario, int Index, double StartupMs, double WorkingSetMb, double FootprintMb, double GcHeapMb,
        double CpuPercent, double AllocMbPerSec, double CharsPerSec, double UiP50, double UiP95, double UiMax, int Gen0, int Gen2,
        int RealizedRows, string Extra, string[] Assemblies);

    public static async Task<List<Run>> RunAsync(string root, string scenario, int runs, string? appPath, string label)
    {
        var app = appPath ?? Path.Combine(root, "src/Wandur.Desktop/bin/Release/net10.0/Wandur");
        if (!File.Exists(app)) throw new FileNotFoundException("Build the client in Release first", app);
        var results = new List<Run>();
        // WANDUR_BENCH_DATA_ROOT moves the throwaway data folders (relative to the repository root).
        var dataRoot = Environment.GetEnvironmentVariable("WANDUR_BENCH_DATA_ROOT") is { Length: > 0 } custom ? custom : ".superpowers/perf";
        var data = Path.Combine(root, dataRoot, $"data-{scenario}");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        for (var i = 0; i < runs; i++)
        {
            var settings = Scenario(scenario);
            MudServer? mud = settings.Rate is { } rate ? new MudServer(0, rate, chat: settings.Chat, writeMs: settings.WriteMs) : null;
            DirectoryServer? directory = null;
            if (settings.Worlds > 0)
            {
                var art = Path.Combine(root, ".superpowers/perf/art");
                Directory.CreateDirectory(art);
                directory = new DirectoryServer(FreePort(), settings.Worlds, Cached(Path.Combine(art, "large.jpg"), () => Artwork.Generate(4000, 3000, SKEncodedImageFormat.Jpeg)),
                    Cached(Path.Combine(art, "large.png"), () => Artwork.Generate(4000, 3000, SKEncodedImageFormat.Png)), Path.Combine(root, "tests/Fixtures/wandur-directory.json"));
            }
            var probe = Path.Combine(root, ".superpowers/perf/logs", $"probe-{label}-{scenario}-{i}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
            File.Delete(probe); File.Delete(probe + ".exit");
            var start = new ProcessStartInfo(app) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(data);
            start.Environment["WANDUR_PERF_PROBE"] = probe;
            start.Environment["WANDUR_PERF_SCENARIO"] = settings.Mode;
            start.Environment["WANDUR_PERF_SECONDS"] = settings.Seconds.ToString(CultureInfo.InvariantCulture);
            start.Environment["WANDUR_PERF_SETTLE"] = settings.Settle.ToString(CultureInfo.InvariantCulture);
            start.Environment["WANDUR_PERF_SESSIONS"] = settings.Sessions.ToString(CultureInfo.InvariantCulture);
            if (mud is not null) start.Environment["WANDUR_PERF_HOST"] = $"127.0.0.1:{mud.Port}";
            // Never the real directory: a closed loopback port, or the fake directory.
            start.Environment["WANDUR_DIRECTORY_URL"] = directory?.Address ?? "http://127.0.0.1:9/";
            using var process = Process.Start(start)!;
            _ = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync();
            var waited = Stopwatch.StartNew();
            double footprint = 0;
            while (waited.Elapsed < TimeSpan.FromSeconds(180) && !process.HasExited)
            {
                if (File.Exists(probe) && File.ReadAllText(probe).Contains("\"phase\":\"done\"", StringComparison.Ordinal))
                {
                    footprint = Footprint(process.Id);
                    break;
                }
                await Task.Delay(200);
            }
            File.WriteAllText(probe + ".exit", "");
            if (!process.WaitForExit(30_000)) { process.Kill(true); process.WaitForExit(); }
            var artRequests = directory?.ArtRequests ?? 0;
            if (mud is not null) await mud.DisposeAsync();
            directory?.Dispose();
            results.Add(Parse(scenario, i, probe, footprint, artRequests));
            File.Delete(probe + ".exit");
        }
        return results;
    }

    private sealed record Settings(string Mode, int Seconds, int Settle, int Sessions, int? Rate, int Worlds, bool Chat = true, int WriteMs = 100);

    private static Settings Scenario(string name) => name switch
    {
        "startup" => new("idle", 5, 3, 0, null, 0),
        "session-idle" => new("sessions", 10, 3, 1, 0, 0),
        "flood-100k" => new("sessions", 10, 3, 1, 100_000, 0),
        "flood-100k-nochat" => new("sessions", 10, 3, 1, 100_000, 0, false),
        "flood-1m" => new("sessions", 10, 3, 1, 1_000_000, 0),
        "flood-1m-nochat" => new("sessions", 10, 3, 1, 1_000_000, 0, false),
        "multi-4" => new("sessions", 10, 3, 4, 50_000, 0),
        "multi-4-nochat" => new("sessions", 10, 3, 4, 50_000, 0, false),
        "multi-8-idle" => new("sessions", 10, 3, 8, 0, 0),
        "directory" => new("directory", 5, 6, 0, null, 300),
        // A steady stream (a write every 2 ms) instead of ten bursts a second, as a busy world sends.
        "steady-1m" => new("sessions", 10, 3, 1, 1_000_000, 0, false, 2),
        "steady-4x250k" => new("sessions", 10, 3, 4, 250_000, 0, false, 2),
        _ => throw new ArgumentException("Unknown scenario " + name)
    };

    private static byte[] Cached(string path, Func<byte[]> make)
    {
        if (File.Exists(path)) return File.ReadAllBytes(path);
        var bytes = make();
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>The physical footprint macOS reports for the process (what Activity Monitor calls Memory), in MB.</summary>
    private static double Footprint(int pid)
    {
        try
        {
            var info = new ProcessStartInfo("/usr/bin/footprint") { RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-f"); info.ArgumentList.Add("bytes"); info.ArgumentList.Add("--noCategories"); info.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
            using var footprint = Process.Start(info)!;
            var text = footprint.StandardOutput.ReadToEnd();
            footprint.WaitForExit();
            var match = FootprintPattern().Match(text);
            return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 1048576.0 : 0;
        }
        catch { return 0; }
    }

    [GeneratedRegex(@"Footprint:\s*(\d+)\s*B")]
    private static partial Regex FootprintPattern();

    private static Run Parse(string scenario, int index, string probe, double footprint, int artRequests)
    {
        double startup = 0;
        JsonElement? end = null, after = null;
        string[] assemblies = [];
        var extra = new List<string>();
        if (File.Exists(probe))
            foreach (var line in File.ReadAllLines(probe))
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement.Clone();
                switch (root.GetProperty("phase").GetString())
                {
                    case "usable": startup = root.GetProperty("startupMs").GetDouble(); break;
                    case "end": end = root; break;
                    case "after-gc": after = root; break;
                    case "assemblies": assemblies = [.. root.GetProperty("assemblies").EnumerateArray().Select(a => a.GetString()!)]; break;
                    case "opened": extra.Add("connected=" + root.GetProperty("sessions").GetInt32()); break;
                    case "catalog": extra.Add($"worlds={root.GetProperty("worlds").GetInt32()}"); break;
                    case "scroll": extra.Add($"pages={root.GetProperty("pages")} mostRealized={root.GetProperty("mostRealized")} realizedAtTop={root.GetProperty("realizedAtTop")}"); break;
                    case "error": extra.Add("ERROR " + root.GetProperty("error").GetString()); break;
                }
            }
        if (artRequests > 0) extra.Add("artRequests=" + artRequests);
        if (end is { } e && e.TryGetProperty("framesPerSec", out var fps))
            extra.Add(string.Create(CultureInfo.InvariantCulture,
                $"frames/s={fps.GetDouble():F1} uiFrames/s={e.GetProperty("uiFramesPerSec").GetDouble():F1} uiRenderMs/s={e.GetProperty("uiRenderMsPerSec").GetDouble():F1} compMs/s={e.GetProperty("compRenderMsPerSec").GetDouble():F1} measures/s={e.GetProperty("measuresPerSec").GetDouble():F1} compUpdates/s={e.GetProperty("compUpdatesPerSec").GetDouble():F1} timers={e.GetProperty("dispatcherTimers").GetInt64()} focus={e.GetProperty("focused").GetString()}"));
        double D(JsonElement? e, string name) => e is { } v ? v.GetProperty(name).GetDouble() : 0;
        int I(JsonElement? e, string name) => e is { } v ? v.GetProperty(name).GetInt32() : 0;
        return new(scenario, index, startup, D(end, "workingSetMb"), footprint, D(after, "gcHeapMb"), D(end, "cpuPercent"), D(end, "allocMbPerSec"),
            D(end, "charsPerSec"), D(end, "uiP50"), D(end, "uiP95"), D(end, "uiMax"), I(end, "gen0"), I(end, "gen2"), I(end, "realizedRows"),
            string.Join(' ', extra), assemblies);
    }
}
