using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Localization;
using Wandur.Core.Settings;
using Wandur.Core.Storage;
using Wandur.Core.Updates;
using Wandur.Desktop.Terminal;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>The update notice and Check for Updates. Every check here is answered by a fake source; nothing leaves the machine.</summary>
[Collection(UiLanguageCollection.Name)]
public sealed class UpdateNoticeTests
{
    [AvaloniaFact]
    public async Task CheckForUpdatesSaysTheLatestVersionShowsTheNoticeOrSaysWandurNetCouldNotBeReached()
    {
        var source = new FakeSource { Version = "0.1.5" };
        await using var fixture = new Fixture(source, "0.1.5");
        var window = fixture.Window;

        var upToDate = await window.CheckForUpdatesNowAsync();
        Assert.Equal(UpdateCheckStatus.UpToDate, upToDate.Status);
        Assert.Equal("You have the latest version, 0.1.5.", upToDate.Heading);
        Assert.False(window.IsUpdateNoticeVisible);
        await Capture(window, fixture, "update-menu-up-to-date.png");

        source.Fail = true;
        var failed = await window.CheckForUpdatesNowAsync();
        Assert.Equal(UpdateCheckStatus.Failed, failed.Status);
        Assert.Equal("Could not reach wandur.net.", failed.Heading);
        Assert.False(window.IsUpdateNoticeVisible);
        await Capture(window, fixture, "update-menu-unreachable.png");

        source.Fail = false;
        source.Version = "0.1.6";
        var available = await window.CheckForUpdatesNowAsync();
        Assert.Equal(UpdateCheckStatus.Available, available.Status);
        Assert.Null(available.Heading);   // The notice is the answer.
        Settle(window);
        Assert.True(window.IsUpdateNoticeVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Wandur Mud Client 0.1.6 is available.");
        Assert.Equal(5, source.Calls);   // Three checks, and the menu command run twice for its dialog.
        // Each check is remembered, so a restart within the day does not ask again.
        Assert.Equal("0.1.6", window.Controller.Settings.LastUpdateCheck?.Version);
    }

    [AvaloniaFact]
    public async Task ABuildFromSourceSaysChecksAreOffAndNeverAsks()
    {
        var source = new FakeSource { Version = "0.1.6" };
        await using var fixture = new Fixture(source, "0.0.0-dev");
        var result = await fixture.Window.CheckForUpdatesNowAsync();
        Assert.Equal(UpdateCheckStatus.Disabled, result.Status);
        Assert.Equal(L.Format(L.UpdateChecksOffInSourceBuild, "0.0.0-dev"), result.Heading);
        await fixture.Window.AutomaticUpdateCheckAsync();
        Assert.Equal(0, source.Calls);
        Assert.False(fixture.Window.IsUpdateNoticeVisible);
    }

    [AvaloniaFact]
    public async Task TheAutomaticCheckRunsOnceADayAndARestartShowsTheRememberedNoticeWithoutAsking()
    {
        var source = new FakeSource { Version = "0.1.6" };
        var clock = new Clock(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var fixture = new Fixture(source, "0.1.5", clock);

        await fixture.Window.AutomaticUpdateCheckAsync();
        await fixture.Window.AutomaticUpdateCheckAsync();
        Assert.Equal(1, source.Calls);
        Settle(fixture.Window);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);

        clock.Now = clock.Now.AddHours(20);
        await fixture.Restart();
        Assert.True(fixture.Window.IsUpdateNoticeVisible);   // From the saved result.
        await fixture.Window.AutomaticUpdateCheckAsync();
        Assert.Equal(1, source.Calls);

        clock.Now = clock.Now.AddHours(4);
        source.Fail = true;
        await fixture.Window.AutomaticUpdateCheckAsync();   // Due again; the failure is silent.
        Assert.Equal(2, source.Calls);
        Assert.Null(fixture.Window.Controller.Notice);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
        await fixture.Window.AutomaticUpdateCheckAsync();   // No retry.
        Assert.Equal(2, source.Calls);
    }

    [AvaloniaFact]
    public async Task SkippingHidesThatVersionForGoodAndANewerOneShowsAgain()
    {
        var source = new FakeSource { Version = "0.1.6" };
        var clock = new Clock(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var fixture = new Fixture(source, "0.1.5", clock);
        await fixture.Window.AutomaticUpdateCheckAsync();
        Settle(fixture.Window);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);

        Click(fixture.Window, "UpdateSkip");
        Assert.False(fixture.Window.IsUpdateNoticeVisible);
        Assert.Equal("0.1.6", fixture.Window.Controller.Settings.SkippedUpdateVersion);

        await fixture.Restart();
        Assert.False(fixture.Window.IsUpdateNoticeVisible);
        clock.Now = clock.Now.AddDays(1);
        await fixture.Window.AutomaticUpdateCheckAsync();
        Assert.False(fixture.Window.IsUpdateNoticeVisible);

        source.Version = "0.1.7";
        clock.Now = clock.Now.AddDays(1);
        await fixture.Window.AutomaticUpdateCheckAsync();
        Settle(fixture.Window);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
        Assert.Contains(fixture.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Wandur Mud Client 0.1.7 is available.");
    }

    [AvaloniaFact]
    public async Task TheNoticeHidesDuringPrivateInputAndNeverTakesFocus()
    {
        var source = new FakeSource { Version = "0.1.6" };
        await using var fixture = new Fixture(source, "0.1.5");
        var window = fixture.Window;
        await window.Controller.StartAsync();
        await window.CheckForUpdatesNowAsync();
        Settle(window);
        Assert.True(window.IsUpdateNoticeVisible);

        window.Controller.SetManualPrivate(true);
        Settle(window);
        Assert.False(window.IsUpdateNoticeVisible);
        window.Controller.SetManualPrivate(false);
        Settle(window);
        Assert.True(window.IsUpdateNoticeVisible);

        foreach (var name in new[] { "UpdateDownload", "UpdateReleaseNotes", "UpdateSkip", "DismissUpdate" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            Assert.False(button.Focusable, name);
            Assert.False(button.IsTabStop, name);
        }
        Assert.Equal(L.UpdateDownload, window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UpdateDownload").Content);

        // The cross hides it for this run only.
        Click(window, "DismissUpdate");
        Assert.False(window.IsUpdateNoticeVisible);
        Assert.Null(window.Controller.Settings.SkippedUpdateVersion);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    public async Task TheNoticeReadsInEveryLanguage(string language)
    {
        var source = new FakeSource { Version = "0.1.6" };
        await using var fixture = new Fixture(source, "0.1.5");
        var window = fixture.Window;
        UiLanguage.Apply(language);
        await window.Controller.StartAsync();
        window.Controller.ShowNotice(null);
        await window.CheckForUpdatesNowAsync();
        Settle(window);
        var text = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "UpdateNoticeText");
        Assert.Equal(L.Format(L.UpdateAvailable, "0.1.6"), text.Text);
        var skip = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UpdateSkip");
        Assert.Equal(L.UpdateSkipVersion, skip.Content);
        Assert.True(text.Bounds.Width > 150);
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, $"update-notice-{language}.png"), new PngBitmapEncoderOptions());
        }
    }

    /// <summary>Runs the menu command and, when WANDUR_CAPTURE_DIR is set, saves its dialog; the dialog is closed either way.</summary>
    private static async Task Capture(MainWindow window, Fixture fixture, string name)
    {
        var pending = window.CheckForUpdatesFromMenuAsync();
        for (var i = 0; i < 50 && window.OwnedWindows.Count == 0; i++) { Settle(window); await Task.Delay(10); }
        var dialog = Assert.Single(window.OwnedWindows);
        Settle(dialog);
        Assert.Equal(L.CheckForUpdatesTitle, dialog.Title);
        if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using var frame = dialog.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, name), new PngBitmapEncoderOptions());
        }
        dialog.Close();
        Settle(window);
        await pending;
        _ = fixture;
    }

    private static void Click(Window window, string name)
    {
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "wandur-update-notice-" + Guid.NewGuid());
        private readonly ClientDatabase _database;
        private readonly SqliteSettingsStore _settings;
        private readonly FakeSource _source;
        private readonly string _version;
        private readonly TimeProvider _clock;
        public MainWindow Window { get; private set; }

        public Fixture(FakeSource source, string version, TimeProvider? clock = null)
        {
            _source = source; _version = version; _clock = clock ?? TimeProvider.System;
            _database = new ClientDatabase(Path.Combine(_directory, "test.db"));
            _settings = new SqliteSettingsStore(_database, Path.Combine(_directory, "legacy.json"));
            Window = Create();
        }

        private MainWindow Create()
        {
            var window = new MainWindow(new TranscriptDisplayFactory(), _settings, new MemoryPasswordVault(),
                new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(),
                updates: new UpdateService(_source, _clock, _version));
            window.Show(); Settle(window);
            return window;
        }

        public async Task Restart()
        {
            await Window.Sessions.DisposeAsync(); Window.Close();
            Window = Create();
        }

        public async ValueTask DisposeAsync()
        {
            await Window.Sessions.DisposeAsync(); Window.Close();
            UiLanguage.Apply("");
            ThemeService.Apply(new());
            _database.Dispose();
            TestFiles.DeleteDirectory(_directory);
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSource : IUpdateSource
    {
        public string Version { get; set; } = "0.1.5";
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task<UpdateInfo> GetLatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("offline");
            return Task.FromResult(new UpdateInfo(Version, UpdateService.DownloadsPage, new Uri($"https://github.com/Wundur-AI/wandur/releases/tag/v{Version}")));
        }
    }
}
