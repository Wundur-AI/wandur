using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;
using Wandur.Models;

namespace Wandur.Desktop.Tests;

/// <summary>The Armored title bar after it was slimmed like Fleet's.</summary>
public sealed class ArmoredSlimTitleBarTests
{
    [Fact]
    public void ArmoredMetricsArePinned()
    {
        // Pinned on purpose: the band was 80, the plaque 86, the title 20 with 1.8 spacing, the icon 32, the
        // toolbar 13/54 and the caption actions 10 from the top before the Armored title bar was slimmed.
        var metrics = TitleBarMetrics.Armored;
        Assert.Equal(60, metrics.BandHeight);
        Assert.Equal(2, metrics.PlaqueTop);
        Assert.Equal(8, metrics.PlaqueDrop);
        Assert.Equal(66, metrics.PlaqueHeight);
        Assert.Equal(13, metrics.TitleFontSize);
        Assert.Equal(1.0, metrics.TitleLetterSpacing);
        Assert.Equal(24, metrics.LogoSize);
        Assert.Equal(100, metrics.TextInset);
        Assert.Equal(13, metrics.ToolbarTopPadding);
        Assert.Equal(54, metrics.ToolbarMinHeight);
        Assert.Equal(14, metrics.HiddenToolbarClearance);
        Assert.Equal(15, metrics.ActionsTop);
        Assert.Equal(new Thickness(24, 0, 24, 30), WindowSkinDefinition.Resolve("Armored").FrameInset);
    }

    [AvaloniaFact]
    public async Task TheIdleArmoredTitleBarShowsTheAppNameInItsShorterBand()
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-armored-slim-" + Guid.NewGuid());
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings { Theme = "Slate", Skin = "Armored", Language = "en", UseWorldThemes = false });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore()) { Width = 1600, Height = 1000 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal("WANDUR MUD CLIENT", Named<TextBlock>(window, "AppTitle").Text);
            Assert.Equal(60, window.GetVisualDescendants().OfType<ThemeWindowSkinHost>().Single().BandHeight);
            Assert.Equal(60, window.ExtendClientAreaTitleBarHeightHint);
            var host = Named<Border>(window, "PlaqueTitleHost");
            Assert.Equal(HorizontalAlignmentCenter, host.HorizontalAlignment);
            Assert.Equal(0, host.Margin.Left);
            Assert.Equal(1600 / 2d, host.TranslatePoint(new Point(host.Bounds.Width / 2, 0), window)!.Value.X, 1);
            Assert.Equal(0, host.Bounds.Width % 2);
            var plaqueBottom = host.TranslatePoint(new Point(0, host.Bounds.Height), window)!.Value.Y;
            Assert.Equal(68, plaqueBottom, 1);
            // The toolbar row starts at the seam, its controls below the projecting plaque.
            var toolbar = Named<Border>(window, "MainToolbar");
            var toolbarTop = toolbar.TranslatePoint(default, window)!.Value.Y;
            Assert.Equal(60, toolbarTop, 1);
            Assert.True(toolbarTop + toolbar.Padding.Top >= plaqueBottom);
            // The caption actions sit centered in the band.
            var actions = Named<StackPanel>(window, "TitleActions");
            Assert.Equal(30, actions.TranslatePoint(default, window)!.Value.Y + actions.Bounds.Height / 2, 1);
            Assert.Equal(24, Named<Image>(window, "TitleBarLogo").Bounds.Height);
            // The icon and title sit centered inside the plate.
            var identity = Named<Grid>(window, "PlaqueIdentity");
            Assert.Equal(host.Bounds.Width / 2, identity.TranslatePoint(new Point(identity.Bounds.Width / 2, 0), host)!.Value.X, 1);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); Directory.Delete(path, true); }
    }

    // Widths where every platform agrees: at 1600 the full name fits even beside the Windows caption buttons,
    // and at 1040 the long world name never fits in full on any platform.
    [AvaloniaTheory]
    [InlineData(1600, "Legends of the Jedi", "WANDUR MUD CLIENT - LEGENDS OF THE JEDI")]
    [InlineData(1040, "Etoiles du Nord: Legends of the Outer Reaches and the Very Distant Stars", "WANDUR - ")]
    public async Task TheArmoredPlateFallsBackToTheShortNameWhenTheFullOneDoesNotFit(int width, string world, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-armored-plate-" + Guid.NewGuid());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var profile = new ConnectionProfile { Name = world, Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port };
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings { Theme = "Hull", Skin = "Armored", Language = "en", UseWorldThemes = false, Profiles = [profile] });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore()) { Width = width, Height = 800 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var opening = window.Sessions.OpenAsync(profile);
            using var peer = await listener.AcceptTcpClientAsync();
            await opening;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var title = Named<TextBlock>(window, "AppTitle");
            Assert.StartsWith(expected, title.Text);
            if (expected.EndsWith(" - ", StringComparison.Ordinal))
                Assert.Equal(expected + world.ToUpperInvariant(), title.Text);
            var host = Named<Border>(window, "PlaqueTitleHost");
            Assert.Equal(width / 2d, host.TranslatePoint(new Point(host.Bounds.Width / 2, 0), window)!.Value.X, 1);
            var origin = title.TranslatePoint(default, host)!.Value;
            Assert.True(origin.X >= 0 && origin.X + title.Bounds.Width <= host.Bounds.Width + .5,
                $"The title {title.Bounds} runs outside its plaque {host.Bounds}.");
            var actions = Named<StackPanel>(window, "TitleActions");
            Assert.True(host.TranslatePoint(new Point(host.Bounds.Width, 0), window)!.Value.X <= actions.TranslatePoint(default, window)!.Value.X);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    private const Avalonia.Layout.HorizontalAlignment HorizontalAlignmentCenter = Avalonia.Layout.HorizontalAlignment.Center;

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
}
