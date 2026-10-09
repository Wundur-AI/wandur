using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Headless;
using Wandur.Bench;

// Usage (from the repository root; see docs/perf.md):
//   dotnet run -c Release --project bench/Wandur.Bench -- micro [--label NAME] [--only terminal,display,directory,sessions,switch,thumbnails]
//   dotnet run -c Release --project bench/Wandur.Bench -- app [--label NAME] [--runs N] startup session-idle flood-100k ...
//   dotnet run -c Release --project bench/Wandur.Bench -- mud-server [--port 4400] [--rate BYTES_PER_SECOND] [--write-ms 100] [--nochat]
//   dotnet run -c Release --project bench/Wandur.Bench -- directory-server [--port 4401] [--worlds 300]
var root = FindRoot();
Micro.Root = root;
var command = args.Length > 0 ? args[0] : "micro";
string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
var label = Option("--label", "run");
var logs = Path.Combine(root, ".superpowers/perf/logs");
Directory.CreateDirectory(logs);

switch (command)
{
    case "micro":
    {
        var only = Option("--only", "terminal,display,directory,sessions,thumbnails").Split(',');
        var results = new List<Measurement>();
        if (only.Contains("thumbnails"))
            foreach (var (format, w, h, bw, bh, cover) in new[] { ("jpeg", 4000, 3000, 800, 320, true), ("png", 4000, 3000, 800, 320, true), ("jpeg", 4000, 3000, 80, 60, false), ("jpeg", 800, 320, 800, 320, true) })
                results.Add(Child(format, w, h, bw, bh, cover));
        // Not disposed: the continuation runs on the session thread, which cannot wait for itself. The process exits below.
        var session = HeadlessUnitTestSession.StartNew(typeof(BenchApp));
        results.AddRange(await session.Dispatch(async () =>
        {
            var list = new List<Measurement>();
            if (only.Contains("terminal")) list.AddRange(Micro.Terminal());
            if (only.Contains("display")) list.AddRange(Micro.Display());
            if (only.Contains("directory")) list.AddRange(await Micro.DirectoryAsync(root));
            if (only.Contains("switch")) list.Add(await Harness.SwitchAsync());
            if (only.Contains("sessions"))
            {
                list.Add(await Harness.SessionsAsync(1, 200_000, 8));
                list.Add(await Harness.SessionsAsync(1, 200_000, 8, chat: false));
                list.Add(await Harness.SessionsAsync(4, 100_000, 8, chat: false));
            }
            return list;
        }, CancellationToken.None));
        var table = Table(results);
        Console.WriteLine(table);
        File.WriteAllText(Path.Combine(logs, $"micro-{label}.md"), table);
        File.WriteAllText(Path.Combine(logs, $"micro-{label}.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        break;
    }
    case "thumb":
    {
        // Not disposed: the continuation runs on the session thread, which cannot wait for itself. The process exits below.
        var session = HeadlessUnitTestSession.StartNew(typeof(BenchApp));
        var m = await session.Dispatch(() => Micro.ThumbnailAsync(args[1], int.Parse(args[2], CultureInfo.InvariantCulture), int.Parse(args[3], CultureInfo.InvariantCulture),
            int.Parse(args[4], CultureInfo.InvariantCulture), int.Parse(args[5], CultureInfo.InvariantCulture), args[6] == "cover", 7), CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(m));
        break;
    }
    case "app":
    {
        var runs = int.Parse(Option("--runs", "3"), CultureInfo.InvariantCulture);
        var scenarios = new List<string>();
        for (var i = 1; i < args.Length; i++) { if (args[i].StartsWith("--", StringComparison.Ordinal)) i++; else scenarios.Add(args[i]); }
        if (scenarios.Count == 0) scenarios = ["startup", "session-idle", "flood-100k", "flood-100k-nochat", "flood-1m-nochat", "multi-4", "multi-4-nochat", "multi-8-idle", "directory"];
        var all = new List<AppRunner.Run>();
        foreach (var scenario in scenarios)
        {
            Console.Error.WriteLine($"scenario {scenario} x{runs}");
            all.AddRange(await AppRunner.RunAsync(root, scenario, runs, Option("--app", null!), label));
        }
        var table = AppTable(all);
        Console.WriteLine(table);
        File.WriteAllText(Path.Combine(logs, $"app-{label}.md"), table);
        File.WriteAllText(Path.Combine(logs, $"app-{label}.json"), JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        break;
    }
    case "mud-server":
    {
        await using var server = new MudServer(int.Parse(Option("--port", "4400"), CultureInfo.InvariantCulture), int.Parse(Option("--rate", "0"), CultureInfo.InvariantCulture),
            chat: !args.Contains("--nochat"), writeMs: int.Parse(Option("--write-ms", "100"), CultureInfo.InvariantCulture));
        Console.WriteLine($"Loopback MUD on 127.0.0.1:{server.Port}; Ctrl+C stops it.");
        await WaitForCancel();
        break;
    }
    case "directory-server":
    {
        var art = Path.Combine(root, ".superpowers/perf/art");
        Directory.CreateDirectory(art);
        using var server = new DirectoryServer(int.Parse(Option("--port", "4401"), CultureInfo.InvariantCulture), int.Parse(Option("--worlds", "300"), CultureInfo.InvariantCulture),
            Artwork.Generate(4000, 3000, SkiaSharp.SKEncodedImageFormat.Jpeg), Artwork.Generate(4000, 3000, SkiaSharp.SKEncodedImageFormat.Png),
            Path.Combine(root, "tests/Fixtures/wandur-directory.json"));
        Console.WriteLine($"Fake directory at {server.Address}; set WANDUR_DIRECTORY_URL to it. Ctrl+C stops it.");
        await WaitForCancel();
        break;
    }
    default:
        Console.Error.WriteLine("Commands: micro, app, mud-server, directory-server");
        return 1;
}
// The headless session leaves a foreground thread behind; leave explicitly once the results are written.
Environment.Exit(0);
return 0;

static Measurement Child(string format, int w, int h, int bw, int bh, bool cover)
{
    _ = Micro.ArtPath(format, w, h);
    var info = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, UseShellExecute = false };
    var entry = typeof(Micro).Assembly.Location;
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet") info.ArgumentList.Add(entry);
    foreach (var a in new[] { "thumb", format, w.ToString(CultureInfo.InvariantCulture), h.ToString(CultureInfo.InvariantCulture), bw.ToString(CultureInfo.InvariantCulture), bh.ToString(CultureInfo.InvariantCulture), cover ? "cover" : "fit" })
        info.ArgumentList.Add(a);
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last();
    return JsonSerializer.Deserialize<Measurement>(line)!;
}

static string Table(List<Measurement> results)
{
    var sb = new StringBuilder();
    sb.AppendLine("| Measurement | Unit | ms per unit | managed KB per unit | Note |");
    sb.AppendLine("|---|---|---:|---:|---|");
    foreach (var m in results)
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {m.Name} | {m.Unit} | {m.MillisecondsPerOp:F3} | {m.AllocatedKbPerOp:F1} | {m.Note} |"));
    return sb.ToString();
}

static string AppTable(List<AppRunner.Run> runs)
{
    var sb = new StringBuilder();
    sb.AppendLine("| Scenario | Run | Startup ms | Working set MB | Footprint MB | GC heap after GC MB | CPU % | Alloc MB/s | Chars/s | UI p50 ms | UI p95 ms | UI max ms | Gen0 | Gen2 | Rows | Notes |");
    sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
    foreach (var r in runs)
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| {r.Scenario} | {r.Index} | {r.StartupMs:F0} | {r.WorkingSetMb:F0} | {r.FootprintMb:F0} | {r.GcHeapMb:F1} | {r.CpuPercent:F1} | {r.AllocMbPerSec:F1} | {r.CharsPerSec:F0} | {r.UiP50:F1} | {r.UiP95:F1} | {r.UiMax:F0} | {r.Gen0} | {r.Gen2} | {r.RealizedRows} | {r.Extra} |"));
    return sb.ToString();
}

static async Task WaitForCancel()
{
    var done = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
    await done.Task;
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wandur.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
