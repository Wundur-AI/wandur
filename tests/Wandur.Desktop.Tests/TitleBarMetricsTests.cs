using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;

namespace Wandur.Desktop.Tests;

/// <summary>Every custom-chrome skin's title bar takes its sizes from that skin's <see cref="TitleBarMetrics"/>.</summary>
public sealed class TitleBarMetricsTests
{
    [Fact]
    public void OnlyTheCustomChromeSkinsHaveTitleBarMetrics()
    {
        Assert.Same(TitleBarMetrics.Fleet, WindowSkinDefinition.Resolve("Fleet").TitleBar);
        Assert.Same(TitleBarMetrics.Armored, WindowSkinDefinition.Resolve("Armored").TitleBar);
        Assert.Null(WindowSkinDefinition.Resolve("System").TitleBar);
        Assert.False(WindowSkinDefinition.Resolve("System").CustomChrome);
        Assert.Equal(0, WindowSkinDefinition.Resolve("System").TitleHeight);
        foreach (var metrics in new[] { TitleBarMetrics.Fleet, TitleBarMetrics.Armored })
        {
            Assert.Equal(metrics.BandHeight - metrics.PlaqueTop + metrics.PlaqueDrop, metrics.PlaqueHeight);
            Assert.True(metrics.PlaqueDrop > 0, "The plaque projects below the band's seam.");
            Assert.True(metrics.ToolbarTopPadding > metrics.PlaqueDrop, "The toolbar row clears the plaque.");
            Assert.True(metrics.HiddenToolbarClearance > metrics.PlaqueDrop, "Content clears the plaque without a toolbar.");
            Assert.InRange(metrics.ActionsTop, 0, metrics.BandHeight - metrics.ActionButtonSize);
        }
    }

    [AvaloniaTheory]
    [InlineData("Fleet")]
    [InlineData("Armored")]
    public async Task TheWindowTitleBarReadsItsSizesFromTheActiveSkinsMetrics(string skin)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-titlebar-metrics-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings { Theme = "Slate", Skin = skin, Language = "en", UseWorldThemes = false, ClassifyRoomsLocally = false });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore()) { Width = 1600, Height = 1000 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var metrics = ThemeService.ActiveWindowSkin.TitleBar!;
            Assert.Same(WindowSkinDefinition.Resolve(skin).TitleBar, metrics);

            var frame = window.GetVisualDescendants().OfType<ThemeWindowSkinHost>().Single();
            Assert.Equal(metrics.BandHeight, frame.BandHeight);
            Assert.Equal(metrics.BandHeight, ThemeService.AppliedSkin!.Layout!.TitleBar!.Height);
            Assert.Equal(metrics.BandHeight, window.ExtendClientAreaTitleBarHeightHint);

            var plaque = Named<Border>(window, "PlaqueTitleHost");
            Assert.Equal(metrics.PlaqueHeight, plaque.Bounds.Height);
            Assert.Equal(metrics.PlaqueTop, plaque.TranslatePoint(default, window)!.Value.Y, 1);
            Assert.Equal(metrics.PlaqueHeight, frame.TitleModuleBounds.Height);

            var title = Named<TextBlock>(window, "AppTitle");
            Assert.Equal(metrics.TitleFontSize, title.FontSize);
            Assert.Equal(metrics.TitleLetterSpacing, title.LetterSpacing);
            Assert.Equal(metrics.LogoSize, Named<Image>(window, "TitleBarLogo").Bounds.Height);

            var toolbar = Named<Border>(window, "MainToolbar");
            Assert.Equal(metrics.ToolbarTopPadding, toolbar.Padding.Top);
            Assert.Equal(metrics.ToolbarMinHeight, toolbar.MinHeight);
            Assert.Equal(metrics.ActionsTop, Named<StackPanel>(window, "TitleActions").TranslatePoint(default, window)!.Value.Y, 1);
        }
        finally
        {
            await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new());
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
}
