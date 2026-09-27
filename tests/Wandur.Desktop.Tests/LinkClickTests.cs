using System.Reflection;
using Avalonia.Controls;
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

    /// <summary>Raises the library's own UrlClicked, as its pointer handling does on a Ctrl+click over a link.</summary>
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
}
