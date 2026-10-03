using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>The Help menu sends people to wandur.net: Getting Started opens the online help and Other MUD Clients
/// the page about other clients, in the system browser. A fake launcher stands in for the browser.</summary>
public sealed class HelpMenuTests
{
    // OfflineDirectory points WANDUR_DIRECTORY_URL at this address for the whole test assembly, and the site's
    // pages follow it, so a test never opens wandur.net itself.
    private static readonly Uri Site = new(OfflineDirectory.Address);

    [AvaloniaFact]
    public void TheHelpMenuOpensTheOnlineHelpAndTheOtherClientsPage()
    {
        var window = NewWindow();
        var launched = new List<Uri>();
        window.LaunchLink = uri => { launched.Add(uri); return Task.FromResult(true); };
        try
        {
            window.Show();
            var help = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == L.Help).Menu!;
            var items = help.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToArray();
            Assert.Equal([L.GettingStarted, L.OtherMudClients, L.AboutWandur], items.Select(i => i.Header));

            items[0].Command!.Execute(null);
            items[1].Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal([new Uri(Site, "client/help"), new Uri(Site, "clients")], launched);
            Assert.All(launched, uri => Assert.NotEqual("www.wandur.net", uri.Host));
            Assert.Null(window.Controller.Notice);

            // The window menu Windows and Linux show carries the same items with the same commands.
            var menu = window.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
            var fallback = menu.Items.OfType<MenuItem>().Single(m => m.Header as string == L.Help);
            var fallbackItems = fallback.Items.OfType<MenuItem>().ToArray();
            Assert.Equal([L.GettingStarted, L.OtherMudClients, L.AboutWandur], fallbackItems.Select(i => i.Header as string));
            // With WANDUR_CAPTURE_DIR set, a picture of the open menu for the change's review.
            menu.IsVisible = true; HelpCapture.Settle(window);
            fallback.IsSubMenuOpen = true; HelpCapture.Settle(window);
            var popup = HelpCapture.BoundsIn(fallbackItems[^1].GetVisualAncestors().OfType<Avalonia.Controls.Border>().Last(), window);
            HelpCapture.CropRect(window, "help-menu-wandur-links.png", new Avalonia.Rect(0, 0, popup.Right + 120, popup.Bottom + 60), fallbackItems[^1]);
            fallback.IsSubMenuOpen = false; HelpCapture.Settle(window);
            fallbackItems[1].Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Uri(Site, "clients"), launched[^1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABrowserThatDoesNotOpenIsANotice()
    {
        var window = NewWindow();
        window.LaunchLink = _ => Task.FromResult(false);
        try
        {
            window.Show();
            var help = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == L.Help).Menu!;
            help.Items.OfType<NativeMenuItem>().Single(i => i.Header == L.GettingStarted).Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(L.LinkNotOpened, window.Controller.Notice);

            window.Controller.ShowNotice(null);
            window.LaunchLink = _ => throw new InvalidOperationException("no browser");
            help.Items.OfType<NativeMenuItem>().Single(i => i.Header == L.OtherMudClients).Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(L.LinkNotOpened, window.Controller.Notice);
        }
        finally { window.Close(); }
    }

    private static MainWindow NewWindow()
    {
        var store = new Wandur.Core.Settings.SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-help-menu-" + Guid.NewGuid(), "settings.json"));
        return new MainWindow(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store,
            new MemoryPasswordVault(), new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());
    }
}
