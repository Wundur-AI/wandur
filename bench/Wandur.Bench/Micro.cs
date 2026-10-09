using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using SkiaSharp;
using Wandur.Core.Discovery;
using Wandur.Core.Terminal;
using Wandur.Desktop;
using Wandur.Desktop.Services;
using Wandur.Desktop.Terminal;

namespace Wandur.Bench;

/// <summary>One measured operation: wall time per op (median of rounds), managed bytes allocated per op.</summary>
public sealed record Measurement(string Name, string Unit, double MillisecondsPerOp, double AllocatedKbPerOp, string Note = "");

/// <summary>
/// In-process measurements of the client's hot paths, using the client's own classes. UI pieces run on Avalonia's
/// headless platform with Skia, as the Desktop tests do. Every input is generated; nothing touches the network.
/// </summary>
public static class Micro
{
    public static string Root { get; set; } = "";

    public static Measurement Time(string name, string unit, int rounds, int opsPerRound, Action op, string note = "")
    {
        op(); // warm up (JIT, caches)
        // Start every measurement from a collected heap, so garbage left by setting up one case is not charged to it.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var samples = new List<double>();
        long allocated = 0;
        for (var r = 0; r < rounds; r++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < opsPerRound; i++) op();
            samples.Add(clock.Elapsed.TotalMilliseconds / opsPerRound);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        samples.Sort();
        return new(name, unit, samples[samples.Count / 2], allocated / 1024.0 / rounds / opsPerRound, note);
    }

    private static object? _kept;

    /// <summary>Managed heap kept alive by what <paramref name="make"/> returns, after full collections, in KB. The
    /// instance is held in a static and released afterwards, so nothing from an earlier case is alive in the "before".</summary>
    public static Measurement Retained(string name, Func<object> make)
    {
        static long Heap()
        {
            // Compacting, including the large object heap, so free space between objects is not counted as kept.
            for (var i = 0; i < 2; i++)
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }
            var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
            return info.HeapSizeBytes - info.FragmentedBytes;
        }
        _kept = null;
        var before = Heap();
        _kept = make();
        var after = Heap();
        _kept = null;
        Heap();
        return new(name, "instance", 0, (after - before) / 1024.0, "retained, not allocated");
    }

    public static List<Measurement> Terminal()
    {
        var results = new List<Measurement>();
        var chunks = new AnsiGenerator().Chunks(100_000, 4096);
        var characters = chunks.Sum(c => c.Length);
        var note = $"100,000 lines, {characters / 1048576.0:F1} M chars in 4 KB chunks";
        results.Add(Time("AnsiTerminal.Append, 100k lines into 2,000 line scrollback", "run", 5, 1, () =>
        {
            var terminal = new AnsiTerminal();
            foreach (var chunk in chunks) terminal.Append(chunk);
        }, note));
        var full = new AnsiTerminal();
        foreach (var chunk in chunks.Take(chunks.Count / 4)) full.Append(chunk);
        results.Add(Time("AnsiTerminal.PlainText, full 2,000 line scrollback", "call", 9, 20, () => _ = full.PlainText.Length));
        var small = new AnsiGenerator(9).Chunks(2, 4096)[0];
        results.Add(Time("AnsiTerminal.Lines after one append (full scrollback)", "append+read", 9, 200, () => { full.Append(small); _ = full.Lines[^1]; }));
        results.Add(Time("AnsiTerminal.Append of one 4 KB chunk (full scrollback)", "chunk", 9, 200, () => full.Append(chunks[7])));
        results.Add(Time("AnsiTerminal.HasText (full scrollback)", "call", 9, 1000, () => _ = full.HasText));
        results.Add(Retained("AnsiTerminal retained, 2,000 lines of generated output", () =>
        {
            var terminal = new AnsiTerminal();
            foreach (var chunk in chunks.Take(chunks.Count / 4)) terminal.Append(chunk);
            return terminal;
        }));
        results.Add(Retained("AnsiTerminal(500) retained, the session transcript model since the bound", () =>
        {
            var terminal = new AnsiTerminal(500);
            foreach (var chunk in chunks.Take(chunks.Count / 4)) terminal.Append(chunk);
            return terminal;
        }));
        return results;
    }

    /// <summary>The display side: the xterm engine behind the transcript, fed through the same AnsiTerminal event.</summary>
    public static List<Measurement> Display()
    {
        var results = new List<Measurement>();
        var chunks = new AnsiGenerator().Chunks(100_000, 4096);
        results.Add(Time("TranscriptDisplay append (AnsiTerminal + xterm), 100k lines", "run", 3, 1, () =>
        {
            var source = new AnsiTerminal();
            using var display = new TranscriptDisplay(source);
            foreach (var chunk in chunks) source.Append(chunk);
        }));
        var full = new AnsiTerminal();
        using var shown = new TranscriptDisplay(full);
        foreach (var chunk in chunks.Take(chunks.Count / 4)) full.Append(chunk);
        results.Add(Time("TranscriptDisplay.PlainText, full scrollback", "call", 7, 5, () => _ = shown.PlainText.Length));
        results.Add(Time("TranscriptDisplay.TopVisibleText", "call", 7, 200, () => _ = shown.TopVisibleText.Length));
        results.Add(Time("TranscriptDisplay.HasText, full scrollback", "call", 7, 200, () => _ = shown.HasText));
        results.Add(Retained("TranscriptDisplay retained (its AnsiTerminal and xterm buffer), 2,000 lines", () =>
        {
            var source = new AnsiTerminal();
            var display = new TranscriptDisplay(source);
            foreach (var chunk in chunks.Take(chunks.Count / 4)) source.Append(chunk);
            return (source, display);
        }));
        return results;
    }

    /// <summary>
    /// Thumbnail decode for a large source, in a fresh process so the peak resident size is this decode's alone.
    /// Returns ms per thumbnail, managed KB per thumbnail and the rise in peak RSS (MB) over the process's own.
    /// </summary>
    public static async Task<Measurement> ThumbnailAsync(string format, int width, int height, int boxWidth, int boxHeight, bool cover, int count)
    {
        // The picture is generated once by the parent and read here, so drawing it never counts toward this process's peak.
        var bytes = File.ReadAllBytes(ArtPath(format, width, height));
        using var server = new DirectoryServer(FreePort(), 1, bytes, bytes, Path.Combine(Root, "tests/Fixtures/wandur-directory.json"));
        var cache = Path.Combine(Path.GetTempPath(), "wandur-bench-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            using var catalog = new WorldCatalog(Path.Combine(cache, "directory.json"), new Uri(server.Address + "/"), new HttpClient());
            await catalog.LoadAsync(true);
            var listing = catalog.Worlds[0];
            // Fill the catalog's disk cache once so every measured load is decode only.
            _ = await catalog.GetArtAsync(listing, WorldArtwork.Generated, WorldCatalog.RowSize);
            GC.Collect(); GC.WaitForPendingFinalizers();
            var baseline = PeakRssMb();
            var samples = new List<double>();
            long allocated = 0;
            for (var i = 0; i < count; i++)
            {
                using var thumbnails = new WorldThumbnails(catalog, boxWidth, boxHeight, cacheBitmaps: false, kind: WorldArtwork.Preferred, cover: cover, size: WorldCatalog.RowSize);
                var before = GC.GetTotalAllocatedBytes(true);
                var clock = Stopwatch.StartNew();
                using var bitmap = await thumbnails.GetAsync(listing);
                samples.Add(clock.Elapsed.TotalMilliseconds);
                allocated += GC.GetTotalAllocatedBytes(true) - before;
                if (bitmap is null) throw new InvalidOperationException("thumbnail failed");
                if (i == 0) Console.Error.WriteLine($"  result {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
            }
            samples.Sort();
            var peak = PeakRssMb() - baseline;
            return new($"Thumbnail {width}x{height} {format.ToUpperInvariant()} to {boxWidth}x{boxHeight}{(cover ? " cover" : "")}", "image",
                samples[samples.Count / 2], allocated / 1024.0 / count, string.Create(CultureInfo.InvariantCulture, $"peak RSS rise {peak:F0} MB; source {bytes.Length / 1024} KB"));
        }
        finally { try { Directory.Delete(cache, true); } catch { } }
    }

    /// <summary>A generated picture of this size and format, made on first use and kept under .superpowers/perf/art.</summary>
    public static string ArtPath(string format, int width, int height)
    {
        var folder = Path.Combine(Root, ".superpowers/perf/art");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"bench-{width}x{height}.{format}");
        if (!File.Exists(path))
            File.WriteAllBytes(path, Artwork.Generate(width, height, format == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg));
        return path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RUsage
    {
        public long UserSec, UserUsec, SystemSec, SystemUsec;
        public long MaxRss, IxRss, IdRss, IsRss, MinFlt, MajFlt, NSwap, InBlock, OuBlock, MsgSnd, MsgRcv, NSignals, NVcsw, NIvcsw;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int getrusage(int who, out RUsage usage);

    /// <summary>Peak resident size of this process so far; macOS reports ru_maxrss in bytes.</summary>
    public static double PeakRssMb() => getrusage(0, out var usage) == 0 ? usage.MaxRss / 1048576.0 : 0;

    public static async Task<List<Measurement>> DirectoryAsync(string root)
    {
        var results = new List<Measurement>();
        using var server = new DirectoryServer(FreePort(), 500, [1], [1], Path.Combine(root, "tests/Fixtures/wandur-directory.json"));
        var cache = Path.Combine(Path.GetTempPath(), "wandur-bench-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            using var catalog = new WorldCatalog(Path.Combine(cache, "directory.json"), new Uri(server.Address + "/"), new HttpClient());
            await catalog.LoadAsync(true);
            var window = Harness.Window(Path.Combine(cache, "settings.json"), catalog);
            var model = window.Sessions.Browser(catalog);
            model.Attach();
            string[] searches = ["", "bench", "world 4", "zzz"];
            var n = 0;
            results.Add(Time($"Directory filter, {catalog.Worlds.Count} worlds, search text changes", "query", 9, 20, () =>
                model.Query = model.Query with { Search = searches[n++ % searches.Length] }));
            results.Add(Time($"WorldCatalog.Search alone, {catalog.Worlds.Count} worlds, same searches", "query", 9, 20, () =>
                _ = catalog.Search(searches[n++ % searches.Length])));
            results.Add(Time($"Directory sort, {catalog.Worlds.Count} worlds, cycling 6 sorts", "query", 9, 20, () =>
                model.Query = model.Query with { Search = "", Sort = n++ % 6 }));
            model.Detach();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally { try { Directory.Delete(cache, true); } catch { } }
        return results;
    }

    internal static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
