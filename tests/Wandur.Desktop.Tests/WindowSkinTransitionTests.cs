using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

public sealed class WindowSkinTransitionTests
{
    internal static MainWindow Create() => new(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(),
        new SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-skin-window-" + Guid.NewGuid(), "settings.json")),
        new MemoryPasswordVault(), new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());
    internal static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    internal static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public async Task EverySkinPairClearsOldChromeWithoutRecreatingTheSession()
    {
        var window = Create();
        try
        {
            window.Show(); Settle(window);
            var controller = window.Controller;
            foreach (var first in WindowSkinId.All)
            foreach (var second in WindowSkinId.All)
            {
                window.Sessions.PreviewAppearanceSettings(new() { Skin = first }); Settle(window);
                window.Sessions.PreviewAppearanceSettings(new() { Skin = second }); Settle(window);
                Assert.Same(controller, window.Controller);
                Assert.True(window.ExtendClientAreaToDecorationsHint);
                Assert.Equal(second != "System", Named<Border>(window, "PlaqueTitleHost").IsEffectivelyVisible);
                Assert.True(Named<StackPanel>(window, "TitleActions").IsEffectivelyVisible);
                var frame = window.GetVisualDescendants().OfType<ThemeWindowSkinHost>().Single();
                Assert.Equal(second == "System" ? 0 : second == "Fleet" ? 50 : 80, frame.BandHeight);
                if (second == "System")
                {
                    Assert.Equal(0, frame.EdgeThickness);
                    Assert.Equal(default, frame.Inset);
                    Assert.Null(frame.BorderBitmap);
                    Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                    var toolbar = Named<Border>(window, "MainToolbar");
                    Assert.Equal(0, toolbar.TranslatePoint(default, window)!.Value.Y);
                    Assert.Equal(48, window.ExtendClientAreaTitleBarHeightHint);
                    Assert.True(toolbar.Padding.Left >= (OperatingSystem.IsMacOS() ? 88 : 12));
                    // The Windows fallback menu lives below the toolbar in the same header.
                    // It must override the ancestor caption role even on a Mac test host.
                    var menu = Assert.Single(window.GetVisualDescendants().OfType<Menu>());
                    Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(menu));
                }
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaTheory]
    [InlineData("Fleet")]
    [InlineData("Armored")]
    [InlineData("System")]
    public async Task FullscreenSwitchAndHiddenToolbarRestoreCurrentSkin(string initial)
    {
        var window = Create();
        try
        {
            window.Show();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = initial }); Settle(window);
            foreach (var target in WindowSkinId.All)
            {
                window.ToggleFullScreen(); Settle(window);
                window.Sessions.PreviewAppearanceSettings(new() { Skin = target });
                window.ToolbarVisible = false; Settle(window);
                Assert.True(Named<Button>(window, "ExitFullScreenButton").IsEffectivelyVisible);
                var frame = window.GetVisualDescendants().OfType<ThemeWindowSkinHost>().Single();
                Assert.Equal(0, frame.BandHeight);
                Assert.Equal(0, frame.EdgeThickness);
                if (target == "System") Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(Named<Border>(window, "MainToolbar").Background);
                window.ToggleFullScreen(); Settle(window);
                Assert.True(window.ExtendClientAreaToDecorationsHint);
                Assert.Equal(target == "System" ? 0 : target == "Fleet" ? 50 : 80, frame.BandHeight);
                window.ToolbarVisible = true; Settle(window);
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }
}
