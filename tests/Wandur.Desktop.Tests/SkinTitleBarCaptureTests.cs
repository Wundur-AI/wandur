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
/// Renders the Fleet and Armored title bars as people see them: a connected Lantern Road session at 1600 by 1000,
/// the top of that window, the 1040 minimum width, and an idle window showing the app's own name. Set
/// WANDUR_CAPTURE_DIR to keep the pictures; the same names before and after a change make them easy to compare.
/// </summary>
public sealed class SkinTitleBarCaptureTests
{
    private const double TopCrop = 180;

    [AvaloniaTheory]
    [InlineData("Armored", "Slate", true)]
    [InlineData("Armored", "Hull", false)]
    [InlineData("Fleet", "Slate", true)]
    public async Task TheTitleBarRendersAtFullAndMinimumWidths(string skin, string theme, bool minimum)
    {
        try { await Capture(skin, theme, minimum); }
        finally { ThemeService.Apply(new()); }
    }

    private static async Task Capture(string skin, string theme, bool minimum)
    {
        var name = $"{skin}-{theme}".ToLowerInvariant();
        await using var session = await LanternRoadSession.OpenAsync(new()
        {
            Theme = theme, Settings = s => s with { Skin = skin, Language = "en", FontSize = 16, UseWorldThemes = false }
        });
        var window = session.Window;
        Assert.Equal(skin, ThemeService.ActiveWindowSkin.Id);
        session.FitMap();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
        Save(window, $"{name}-1600.png");
        HelpCapture.CropRect(window, $"{name}-1600-top.png", new Rect(0, 0, window.Bounds.Width, TopCrop));
        AssertTitleFits(window);
        if (!minimum) return;

        window.Width = 1040; window.Height = 1000;
        await session.SettleAsync(() => false, 5);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
        Assert.Equal(1040, window.Bounds.Width);
        Save(window, $"{name}-1040.png");
        HelpCapture.CropRect(window, $"{name}-1040-top.png", new Rect(0, 0, window.Bounds.Width, TopCrop));
        AssertTitleFits(window);
    }

    [AvaloniaTheory]
    [InlineData("Armored")]
    [InlineData("Fleet")]
    public async Task TheIdleTitleBarShowsTheAppName(string skin)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-skin-titlebar-idle-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings { Theme = "Slate", Skin = skin, Language = "en", FontSize = 16,
            UseWorldThemes = false, ClassifyRoomsLocally = false });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore()) { Width = 1600, Height = 1000 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Assert.Equal(skin, ThemeService.ActiveWindowSkin.Id);
            HelpCapture.CropRect(window, $"{skin.ToLowerInvariant()}-slate-idle-top.png", new Rect(0, 0, window.Bounds.Width, TopCrop));
            AssertTitleFits(window);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); Directory.Delete(path, true); ThemeService.Apply(new()); }
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
        if (HelpCapture.Directory is not { } directory) return;
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name), new PngBitmapEncoderOptions());
    }
}
