using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;

namespace Wandur.Desktop.Tests;

public sealed class SystemSkinGroundTests
{
    [AvaloniaTheory]
    [InlineData("Linen")]
    [InlineData("Paper")]
    [InlineData("Midnight")]
    [InlineData("Hull")]
    public void SystemSkinGapsUseThePanelColourNotTheDarkChassis(string theme)
    {
        try
        {
            ThemeService.Apply(new ClientSettings { Theme = theme, Skin = WindowSkinId.System, UseWorldThemes = false });
            var panel = UserTheme.FromPreset(theme).Colors["Panel"];
            var ground = ThemeService.AppliedSkin!.Surfaces!.Ground!;
            Assert.Equal(panel, ground.From);
            Assert.Equal(panel, ground.To);
        }
        finally { ThemeService.Apply(new ClientSettings()); }
    }

    [AvaloniaFact]
    public void DrawnSkinsKeepTheirChassis()
    {
        try
        {
            ThemeService.Apply(new ClientSettings { Theme = "Linen", Skin = WindowSkinId.Fleet, UseWorldThemes = false });
            Assert.NotEqual(UserTheme.FromPreset("Linen").Colors["Panel"], ThemeService.AppliedSkin!.Surfaces!.Ground!.From);
        }
        finally { ThemeService.Apply(new ClientSettings()); }
    }

    [AvaloniaTheory]
    [InlineData(WindowSkinId.System, 0d, 0d)]
    [InlineData(WindowSkinId.Armored, 1d, null)]  // Armored sets its own margin
    public async Task TheDocumentFrameBlendsOnlyOnTheSystemSkin(string skin, double thickness, double? margin)
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-blend-" + Guid.NewGuid());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var profile = new ConnectionProfile { Name = "Blend", Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port };
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings { Theme = "Linen", Skin = skin, UseWorldThemes = false, Profiles = [profile] });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore()) { Width = 1300, Height = 820 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var opening = window.Sessions.OpenAsync(profile);
            using var peer = await listener.AcceptTcpClientAsync();
            await opening;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var document = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentControl>().First();
            var frame = document.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Border");
            Assert.Equal(new Thickness(thickness), frame.BorderThickness);
            if (margin is { } m) { Assert.Equal(m, frame.Margin.Top); Assert.Equal(m, frame.Margin.Bottom); }
        }
        finally { window.Close(); ThemeService.Apply(new ClientSettings()); try { Directory.Delete(path, true); } catch { } }
    }
}
