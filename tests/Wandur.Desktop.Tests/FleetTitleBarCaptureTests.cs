using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;

namespace Wandur.Desktop.Tests;

/// <summary>
/// Renders the Fleet title bar as people see it: a connected Lantern Road session at full width, the top of that
/// window, the compact width, and an idle window showing the app's own name. Set WANDUR_CAPTURE_DIR to keep them.
/// </summary>
public sealed class FleetTitleBarCaptureTests
{
    [AvaloniaTheory]
    [InlineData("Slate")]
    [InlineData("Midnight")]
    public async Task TheFleetTitleBarRendersAtFullAndCompactWidths(string theme)
    {
        var name = theme.ToLowerInvariant();
        await using var session = await LanternRoadSession.OpenAsync(new() { Theme = theme });
        var window = session.Window;
        session.FitMap();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
        Save(window, $"titlebar-{name}-1600.png");
        HelpCapture.CropRect(window, $"titlebar-{name}-1600-top.png", new Rect(0, 0, window.Bounds.Width, 140));
        AssertTitleFits(window);

        // The real minimum width is 1040; lower it here so the capture shows the compact layout at 1000.
        window.MinWidth = 0; window.MinHeight = 0;
        window.Width = 1000; window.Height = 700;
        await session.SettleAsync(() => false, 5);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
        Assert.Equal(1000, window.Bounds.Width);
        Save(window, $"titlebar-{name}-1000.png");
        HelpCapture.CropRect(window, $"titlebar-{name}-1000-top.png", new Rect(0, 0, window.Bounds.Width, 140));
        AssertTitleFits(window);
    }

    [AvaloniaTheory]
    [InlineData("Slate")]
    [InlineData("Midnight")]
    public async Task TheIdleFleetTitleBarShowsTheAppName(string theme)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-titlebar-idle-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings { Theme = theme, Language = "en", UseWorldThemes = false, ClassifyRoomsLocally = false });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore()) { Width = 1600, Height = 1000 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            HelpCapture.CropRect(window, $"titlebar-{theme.ToLowerInvariant()}-idle-top.png", new Rect(0, 0, window.Bounds.Width, 140));
            AssertTitleFits(window);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); Directory.Delete(path, true); }
    }

    private static void AssertTitleFits(Window window)
    {
        var title = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "AppTitle");
        var host = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PlaqueTitleHost");
        Assert.True(title.IsEffectivelyVisible);
        var origin = title.TranslatePoint(default, host)!.Value;
        Assert.True(origin.X >= 0 && origin.X + title.Bounds.Width <= host.Bounds.Width + .5,
            $"The title {title.Bounds} runs outside its plaque {host.Bounds}.");
    }

    private static void Save(Window window, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name), new PngBitmapEncoderOptions());
    }
}
