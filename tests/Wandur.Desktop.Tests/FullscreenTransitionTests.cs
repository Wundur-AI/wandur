using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Wandur.Desktop.Tests;

public sealed class FullscreenTransitionTests
{
    [AvaloniaTheory]
    [InlineData("Fleet")]
    [InlineData("Armored")]
    [InlineData("System")]
    public async Task EnteringClearsChromeAndLaysOutContentBeforeTheStateNotification(string skin)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = skin });
            WindowSkinTransitionTests.Settle(window);
            var frame = window.GetVisualDescendants().OfType<ThemeWindowSkinHost>().Single();
            var title = WindowSkinTransitionTests.Named<TextBlock>(window, "AppTitle");
            var observed = false;
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property != Window.WindowStateProperty || window.WindowState != WindowState.FullScreen) return;
                observed = true;
                Assert.False(title.IsEffectivelyVisible);
                Assert.Equal(0, frame.BandHeight);
                Assert.Equal(default, frame.Inset);
                Assert.Equal(0, frame.Child!.Bounds.Top);
                Assert.Equal(0, window.ExtendClientAreaTitleBarHeightHint);
            };
            window.ToggleFullScreen();
            Assert.True(observed);
            Assert.True(WindowSkinTransitionTests.Named<Button>(window, "ExitFullScreenButton").IsEffectivelyVisible);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public async Task ResizingFullscreenDoesNotClearAndRebindItsToolbarBackground()
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            window.ToggleFullScreen(); WindowSkinTransitionTests.Settle(window);
            var toolbar = WindowSkinTransitionTests.Named<Border>(window, "MainToolbar");
            var backgroundChanges = 0;
            toolbar.PropertyChanged += (_, e) => { if (e.Property == Border.BackgroundProperty) backgroundChanges++; };
            window.Width += 80; WindowSkinTransitionTests.Settle(window);
            window.Height += 40; WindowSkinTransitionTests.Settle(window);
            Assert.Equal(0, backgroundChanges);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public async Task NativeRejectionRestoresChromeAndRapidTogglesPreserveTheWindowedState(WindowState initial)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            window.Sessions.PreviewAppearanceSettings(new() { Skin = "Armored" });
            window.WindowState = initial; WindowSkinTransitionTests.Settle(window);
            window.ToggleFullScreen();
            // A platform correction reports the real state when an entry was refused.
            // Avalonia 12 makes the platform callback internal. Invoke the same
            // receiver without adding a test-only entry point to MainWindow.
            typeof(Window).GetMethod("HandleWindowStateChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, [initial]);
            WindowSkinTransitionTests.Settle(window);
            Assert.Equal(initial, window.WindowState);
            Assert.True(WindowSkinTransitionTests.Named<Border>(window, "PlaqueTitleHost").IsEffectivelyVisible);
            Assert.False(WindowSkinTransitionTests.Named<Button>(window, "ExitFullScreenButton").IsEffectivelyVisible);
            window.ToggleFullScreen(); window.ToggleFullScreen();
            WindowSkinTransitionTests.Settle(window);
            Assert.Equal(initial, window.WindowState);
            Assert.True(WindowSkinTransitionTests.Named<Border>(window, "PlaqueTitleHost").IsEffectivelyVisible);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }
}
