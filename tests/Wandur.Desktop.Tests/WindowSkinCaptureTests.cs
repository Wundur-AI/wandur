using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

public sealed class WindowSkinCaptureTests
{
    [AvaloniaTheory]
    [InlineData("Fleet")]
    [InlineData("Armored")]
    [InlineData("System")]
    public async Task LongLiveWorldTitleKeepsLogoAndActionsSeparateAtMinimumWidth(string skin)
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var profile = new ConnectionProfile
        {
            Name = string.Join(" ", Enumerable.Repeat("The exceptionally long name of a distant world", 3))[..100],
            Host = "127.0.0.1", Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port
        };
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show();
            var opening = window.Sessions.OpenAsync(profile);
            using var connection = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await opening;
            window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = "Slate" });
            window.Width = 1040; window.SetRenderScaling(2);
            WindowSkinTransitionTests.Settle(window);
            var title = WindowSkinTransitionTests.Named<TextBlock>(window, "AppTitle");
            Assert.True(title.IsEffectivelyVisible);
            Assert.Contains("exceptionally long", title.Text!, StringComparison.OrdinalIgnoreCase);
            var logo = WindowSkinTransitionTests.Named<Image>(window, skin == "System" ? "AppLogo" : "TitleBarLogo");
            var titleOrigin = title.TranslatePoint(default, window)!.Value;
            var logoOrigin = logo.TranslatePoint(default, window)!.Value;
            Assert.True(logoOrigin.X + logo.Bounds.Width <= titleOrigin.X);
            Assert.True(title.Bounds.Width > 0);
            Control actions = skin == "System"
                ? WindowSkinTransitionTests.Named<ComboBox>(window, "ToolbarWorlds")
                : WindowSkinTransitionTests.Named<StackPanel>(window, "TitleActions");
            Assert.True(titleOrigin.X + title.Bounds.Width <= actions.TranslatePoint(default, window)!.Value.X);
            Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, title.TextTrimming);
            Capture(window, $"skin-{skin}-long-title-1040-2x");
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public async Task DirectoryPaintsItsReadableSurfaceInsteadOfExposingTheDarkChassis()
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            var browser = Assert.Single(window.GetVisualDescendants().OfType<Wandur.Desktop.Views.WorldBrowserView>());
            Assert.NotNull(browser.Background);
            Assert.True(ContrastProbe.Contrast(ContrastProbe.Resource("MutedBrush"),
                ContrastProbe.Surface(browser, ContrastProbe.Resource("ShellBrush"))) >= 4.5);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Fleet")]
    [InlineData("Armored")]
    [InlineData("System")]
    public async Task PalettesAndRealRenderScalesRemainReadable(string skin)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            // Exercise the renderer below the normal 1040-DIP interactive minimum too.
            window.MinWidth = 800;
            window.Show(); await window.Controller.StartAsync();
            foreach (var palette in new[] { "Hull", "Slate", "Paper", "World", "Hull" })
            {
                var world = palette == "World" ? JsonSerializer.Deserialize<WorldTheme>(File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "world-theme-metallic.json"))) : null;
                window.Controller.StageWorldTheme(world);
                window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = palette == "World" ? "Slate" : palette, UseWorldThemes = world is not null });
                foreach (var width in new[] { 800, 1536 })
                foreach (var scale in new[] { 1d, 2d })
                {
                    window.Width = width; window.Height = 900;
                    window.SetRenderScaling(scale);
                    WindowSkinTransitionTests.Settle(window);
                    Assert.Equal(width, window.ClientSize.Width);
                    Assert.Equal(scale, window.RenderScaling);
                    // All three skins must retain readable instrument text and input carets.
                    var failures = ContrastProbe.Scan(window);
                    Assert.True(failures.Count == 0, $"{skin}/{palette}/{width}/{scale}:\n" + string.Join("\n", failures.Distinct().Take(15)));
                    foreach (var box in window.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible))
                    {
                        var ink = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(box.CaretBrush).Color;
                        Assert.True(ContrastProbe.Contrast(ink, ContrastProbe.Surface(box, ContrastProbe.Resource("ShellBrush"))) >= 4.5);
                    }
                    Capture(window, $"skin-{skin}-{palette}-{width}-{scale}x");
                }
                window.Controller.Pages.SelectedPage = Wandur.Desktop.ViewModels.SessionPage.Diagnostics;
                WindowSkinTransitionTests.Settle(window);
                var diagnosticsFailures = ContrastProbe.Scan(window);
                Assert.True(diagnosticsFailures.Count == 0, $"{skin}/{palette} diagnostics:\n" + string.Join("\n", diagnosticsFailures.Distinct().Take(15)));
                Capture(window, $"skin-{skin}-{palette}-diagnostics");
                window.Controller.Pages.SelectedPage = Wandur.Desktop.ViewModels.SessionPage.Play;
            }
            // Native-caption floating content uses the same resources, without a main-window plaque.
            window.Workspace.FloatDockable(window.Workspace.WorldsTool!);
            WindowSkinTransitionTests.Settle(window);
            var floating = Assert.Single(window.Workspace.HostWindows.OfType<Window>());
            WindowSkinTransitionTests.Settle(floating);
            Capture(floating, $"skin-{skin}-floating");
            window.ResetLayout();
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    private static void Capture(Window window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        Assert.Equal((int)Math.Round(window.ClientSize.Width * window.RenderScaling), frame.PixelSize.Width);
        Assert.Equal((int)Math.Round(window.ClientSize.Height * window.RenderScaling), frame.PixelSize.Height);
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path);
        frame.Save(Path.Combine(path, name + ".png"), new PngBitmapEncoderOptions());
    }
}
