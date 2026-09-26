using System.Diagnostics;
using System.Net;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Wandur.Core.Discovery;
using Wandur.Desktop.Services;

namespace Wandur.Desktop.Tests;

/// <summary>
/// Reads the live public directory at https://api.wandur.net and fetches art for the first twelve worlds the list
/// would show, exactly as the rows do. Read-only; skipped unless WANDUR_LIVE=1. It writes nothing outside a temp
/// folder and never touches the owner's client database.
/// </summary>
public sealed class LiveDirectoryArtDiagnosticTests(ITestOutputHelper output)
{
    private static bool Live => Environment.GetEnvironmentVariable("WANDUR_LIVE") == "1";

    [AvaloniaFact]
    public async Task LiveDirectoryArtLoadsForTheFirstRows()
    {
        if (!Live) { output.WriteLine("Skipped: set WANDUR_LIVE=1 to run against https://api.wandur.net."); return; }
        var directory = Path.Combine(Path.GetTempPath(), "wandur-live-art-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var recorder = new Recorder(new HttpClientHandler());
        using var http = new HttpClient(recorder) { Timeout = TimeSpan.FromMinutes(15) };
        using var catalog = new WorldCatalog(Path.Combine(directory, "directory.json"), new Uri("https://api.wandur.net/"), http);
        try
        {
            var clock = Stopwatch.StartNew();
            await catalog.LoadAsync(force: true);
            output.WriteLine($"directory: {catalog.Worlds.Count} worlds in {clock.ElapsedMilliseconds} ms, warning {catalog.Warning ?? "none"}");
            // The list's default order with nothing typed: the directory's rank, as the view model sorts it.
            var first = catalog.Search("").Where(w => !w.IsAdult).OrderBy(w => w.Community.Rank is null).ThenBy(w => w.Community.Rank).Take(12).ToArray();
            using var thumbnails = new WorldThumbnails(catalog, 800, 320, cacheBitmaps: false, kind: WorldArtwork.Preferred, cover: true, size: WorldCatalog.RowSize);
            // The rows ask for all of their pictures at once, as a list realizing twelve rows does.
            clock.Restart();
            var loads = first.Select(async world =>
            {
                var bitmap = await thumbnails.GetAsync(world);
                var done = clock.ElapsedMilliseconds;
                using (bitmap) return (world, done, ok: bitmap is not null, size: bitmap?.PixelSize.ToString() ?? "-");
            }).ToArray();
            var results = await Task.WhenAll(loads);
            foreach (var (world, done, ok, size) in results)
            {
                var requests = recorder.Log.Where(r => r.Url.Contains(world.Id, StringComparison.OrdinalIgnoreCase)
                    || (world.BannerUrl.Length > 0 && r.Url == world.BannerUrl)).ToArray();
                foreach (var request in requests)
                    output.WriteLine($"{world.Id}: GET {request.Url} -> {request.Status} {request.Bytes} bytes in {request.Milliseconds} ms{(request.Error is { } e ? " error " + e : "")}");
                if (requests.Length == 0) output.WriteLine($"{world.Id}: no request (banner '{world.BannerUrl}', generated '{world.GeneratedArtworkPath}')");
                output.WriteLine($"{world.Id}: bitmap {(ok ? "ok " + size : "NULL")}, finished {done} ms after the rows asked");
            }
            output.WriteLine($"all twelve in {clock.ElapsedMilliseconds} ms, {results.Count(r => r.ok)} of {results.Length} with a picture");
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>The list at 1280 with the live directory and its real art, saved as directory-live-1280.png when
    /// WANDUR_CAPTURE_DIR is set. The settings and caches live in a temp folder.</summary>
    [AvaloniaFact]
    public async Task LiveDirectoryListCapture()
    {
        if (!Live) { output.WriteLine("Skipped: set WANDUR_LIVE=1 to run against https://api.wandur.net."); return; }
        var directory = Path.Combine(Path.GetTempPath(), "wandur-live-capture-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var recorder = new Recorder(new HttpClientHandler());
        using var http = new HttpClient(recorder) { Timeout = TimeSpan.FromMinutes(2) };
        using var catalog = new WorldCatalog(Path.Combine(directory, "directory.json"), new Uri("https://api.wandur.net/"), http);
        await catalog.LoadAsync(force: true);
        var store = new Wandur.Core.Settings.SettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(new Wandur.Core.Settings.ClientSettings { Theme = "Ember", UseWorldThemes = false });
        var sessions = new SessionWorkspace(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: catalog);
        var browser = new Wandur.Desktop.Views.WorldBrowserView(sessions.Browser(catalog), catalog);
        var window = new Avalonia.Controls.Window { Content = browser, Width = 1280, Height = 1000 };
        try
        {
            window.Show();
            var clock = Stopwatch.StartNew();
            Avalonia.Controls.Image[] Plates() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(browser)
                .OfType<Avalonia.Controls.Image>().Where(i => i.Name == "DirectoryRowArtwork").ToArray();
            while (clock.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(100);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                if (recorder.Log.Count > 1 && Plates().Length > 0 && recorder.Log.Count(r => r.Url.Contains("/art") || r.Url.Contains("assets")) >= Plates().Length
                    && Plates().All(p => p.Source is not null || p.IsVisible)) break;
            }
            await Task.Delay(500);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            foreach (var request in recorder.Log) output.WriteLine($"GET {request.Url} -> {request.Status} {request.Bytes} bytes in {request.Milliseconds} ms");
            output.WriteLine($"rows with a picture: {Plates().Count(p => p.Source is not null)} of {Plates().Length}");
            using var frame = window.CaptureRenderedFrame();
            if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } captures && frame is not null)
            {
                Directory.CreateDirectory(captures);
                frame.Save(Path.Combine(captures, "directory-live-1280.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); await sessions.DisposeAsync(); Directory.Delete(directory, true); }
    }

    internal sealed class Recorder(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<(string Url, int Status, long Bytes, long Milliseconds, string? Error)> Log { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                var bytes = response.Content.Headers.ContentLength ?? -1;
                lock (Log) Log.Add((request.RequestUri!.AbsoluteUri, (int)response.StatusCode, bytes, clock.ElapsedMilliseconds, null));
                return response;
            }
            catch (Exception ex)
            {
                lock (Log) Log.Add((request.RequestUri!.AbsoluteUri, 0, 0, clock.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message));
                throw;
            }
        }
    }
}
