using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.Views;
using L = Wandur.Core.Localization.Strings;
using Surface = Iciclecreek.Terminal.TerminalView;

namespace Wandur.Desktop.Tests;

/// <summary>A Ctrl+click on a link in the transcript asks first, and only ever offers plain web addresses.</summary>
public sealed class LinkClickTests
{
    private static WorkspaceController NewController() => new(
        new TranscriptDisplayFactory(), new SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-links-" + Guid.NewGuid() + ".json")),
        new MemoryPasswordVault(), new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>
    /// Raises the library's own UrlClicked, as its pointer handling does on a Ctrl+click over a link. The library has no
    /// public way to raise it, so this reads the event's private backing field: brittle across library upgrades, and
    /// the only place in the tests that does it. If it breaks after an upgrade, check the event still exists and is
    /// still raised only on Ctrl+click.
    /// </summary>
    private static void ClickLinkInLibrary(Window window, string url)
    {
        var surface = Assert.Single(window.GetVisualDescendants().OfType<Surface>());
        var field = typeof(Surface).GetField("UrlClicked", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handler = (EventHandler<Iciclecreek.Terminal.UrlClickedEventArgs>?)field.GetValue(surface);
        Assert.NotNull(handler);
        handler!(surface, new Iciclecreek.Terminal.UrlClickedEventArgs(url));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task AWebLinkAsksBeforeOpeningAndOpensOnlyOnOpen()
    {
        await using var controller = NewController();
        var view = new TerminalView(controller);
        var launched = new List<Uri>();
        view.LaunchLink = uri => { launched.Add(uri); return Task.FromResult(true); };
        var window = new Window { Width = 900, Height = 550, Content = view };
        window.Show();
        try
        {
            var bar = Named<Border>(window, "LinkConfirmation");
            Assert.False(bar.IsVisible);
            ClickLinkInLibrary(window, "https://www.wandur.net/worlds?id=7");
            Assert.True(bar.IsVisible);
            Assert.Equal("https://www.wandur.net/worlds?id=7", Named<TextBlock>(window, "LinkConfirmationUrl").Text);
            Assert.Empty(launched);

            Named<Button>(window, "LinkCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(bar.IsVisible);
            Assert.Empty(launched);

            ClickLinkInLibrary(window, "http://example.test/page");
            Named<Button>(window, "LinkOpen").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(bar.IsVisible);
            Assert.Equal([new Uri("http://example.test/page")], launched);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.test")]
    [InlineData("https://user:secret@example.test/")]
    [InlineData("https://bank.example@evil.example/login")]
    [InlineData("https://wandur.net\u202egnp.exe")]
    [InlineData("https://\u0430pple.com/")]
    [InlineData("https://app\u200ble.com/")]
    [InlineData("https://example\uff0ecom/")]
    public async Task OtherSchemesAndUserInfoAreRefusedWithoutAsking(string url)
    {
        await using var controller = NewController();
        var view = new TerminalView(controller);
        var launched = new List<Uri>();
        view.LaunchLink = uri => { launched.Add(uri); return Task.FromResult(true); };
        var window = new Window { Width = 900, Height = 550, Content = view };
        window.Show();
        try
        {
            ClickLinkInLibrary(window, url);
            Assert.False(Named<Border>(window, "LinkConfirmation").IsVisible);
            Assert.Null(view.PendingLink);
            Assert.Equal(L.LinkRefused, controller.Notice);
            await view.OpenPendingLinkAsync();
            Assert.Empty(launched);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://[::1]/")]
    public async Task LocalAndPrivateHostsAreRefusedWithTheirOwnNotice(string url)
    {
        await using var controller = NewController();
        var view = new TerminalView(controller);
        var window = new Window { Width = 900, Height = 550, Content = view };
        window.Show();
        try
        {
            ClickLinkInLibrary(window, url);
            Assert.False(Named<Border>(window, "LinkConfirmation").IsVisible);
            Assert.Equal(L.LinkRefusedLocal, controller.Notice);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task APendingLinkIsDroppedWhenANewSessionStarts()
    {
        await using var controller = NewController();
        var view = new TerminalView(controller);
        var window = new Window { Width = 900, Height = 550, Content = view };
        window.Show();
        try
        {
            Assert.True(view.RequestOpenLink("https://example.test/"));
            Assert.True(Named<Border>(window, "LinkConfirmation").IsVisible);
            await controller.StartAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(Named<Border>(window, "LinkConfirmation").IsVisible);
            Assert.Null(view.PendingLink);
        }
        finally { window.Close(); }
    }

    /// <summary>Cmd+click on macOS, found by the client's own lookup of the link under the pointer; also the hover hint.</summary>
    [AvaloniaFact]
    public async Task CommandClickOnALinkAsksAndHoverNamesTheShortcut()
    {
        var previous = MudTerminalSurface.CommandClickOpensLinks;
        MudTerminalSurface.CommandClickOpensLinks = true;
        await using var controller = NewController();
        var view = new TerminalView(controller);
        var window = new Window { Width = 900, Height = 550, Content = view };
        window.Show();
        try
        {
            controller.Terminal.Append("See https://www.wandur.net/help, then come back.\r\n");
            controller.FlushOutput();
            Dispatcher.UIThread.RunJobs();
            var surface = window.GetVisualDescendants().OfType<MudTerminalSurface>().Single();
            var row = surface.Terminal.Buffer.ViewportY;
            var line = surface.Terminal.Buffer.GetLine(row)!.TranslateToString(true, 0, surface.Terminal.Cols);
            var column = line.IndexOf("wandur", StringComparison.Ordinal);
            Assert.True(column > 0, line);
            var point = new Avalonia.Point(Math.Max(0, surface.GutterWidth) + (column + 0.5) * surface.CharWidth, (row - surface.Terminal.Buffer.ViewportY + 0.5) * surface.CharHeight);
            Assert.Equal("https://www.wandur.net/help", surface.LinkAt(point));
            Assert.Null(surface.LinkAt(new Avalonia.Point(Math.Max(0, surface.GutterWidth) + 0.5 * surface.CharWidth, point.Y)));

            var inWindow = surface.TranslatePoint(point, window)!.Value;
            window.MouseMove(inWindow, Avalonia.Input.RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(L.LinkClickHintMac, ToolTip.GetTip(surface));

            window.MouseDown(inWindow, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Meta);
            window.MouseUp(inWindow, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Meta);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Named<Border>(window, "LinkConfirmation").IsVisible);
            Assert.Equal(new Uri("https://www.wandur.net/help"), view.PendingLink);
        }
        finally { window.Close(); MudTerminalSurface.CommandClickOpensLinks = previous; }
    }
}
