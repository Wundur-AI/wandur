using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.History;
using Wandur.Core.Localization;
using Wandur.Core.Settings;
using Wandur.Core.Storage;
using Wandur.Desktop.Terminal;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class HistoryNoticeTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    public async Task NoticeFollowsLivePaletteChangesInsteadOfKeepingWarningColors(string language)
    {
        await using var fixture = new Fixture();
        var window = fixture.Window;
        window.Width = 1040;
        await window.Controller.StartAsync();
        UiLanguage.Apply(language);
        foreach (var theme in new[] { "Slate", "Hull", "Paper" })
        {
            window.Sessions.PreviewAppearanceSettings(window.Controller.Settings with { Theme = theme, Skin = "Armored" });
            Settle(window);
            var label = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == L.HistoryRecordingNotice);
            var banner = label.GetVisualAncestors().OfType<Border>().First();
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(banner.Background).Color;
            var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
            Assert.Equal(ContrastProbe.Resource("PanelBrush"), background);
            Assert.Equal(ContrastProbe.Resource("TextBrush"), foreground);
            Assert.True(ContrastProbe.Contrast(foreground, background) >= 4.5);
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "HideHistoryRecordingNotice");
            Assert.True(label.Bounds.Width > 200);
            Assert.True(checkbox.Bounds.Width > 80);
            Assert.True(label.Bounds.Right <= checkbox.Bounds.Left);
            Assert.True(checkbox.Bounds.Right < banner.Bounds.Width);
            Assert.Equal(L.DontShowAgain, checkbox.Content);
            if (Environment.GetEnvironmentVariable("WANDUR_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"notice-{theme}-{language}.png"), new PngBitmapEncoderOptions());
            }
        }
    }

    [AvaloniaFact]
    public async Task FailedPreferenceSaveDoesNotPretendTheReminderWasDisabled()
    {
        await using var fixture = new Fixture();
        var window = fixture.Window;
        await window.Controller.StartAsync();
        Settle(window);
        fixture.Settings.FailSaves = true;
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "HideHistoryRecordingNotice").IsChecked = true;
        Assert.False(window.Controller.Settings.HideHistoryRecordingNotice);
        Assert.Equal(L.HistoryNoticePreferenceFailed, window.Controller.Notice);
        fixture.Settings.FailSaves = false;
        await fixture.Restart();
        await fixture.Window.Controller.StartAsync();
        Assert.Equal(L.HistoryRecordingNotice, fixture.Window.Controller.Notice);
    }

    [AvaloniaFact]
    public async Task DontShowAgainSurvivesRestartAndOtherTabsWithoutDisablingRecordingOrErrors()
    {
        await using var fixture = new Fixture();
        var window = fixture.Window;
        await window.Controller.StartAsync();
        var first = window.Controller;
        await window.Sessions.OpenAsync();
        Settle(window);
        var checkbox = window.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.Name == "HideHistoryRecordingNotice");
        Assert.NotNull(checkbox);
        Assert.True(checkbox.IsEffectivelyVisible);
        checkbox.IsChecked = true;
        Settle(window);
        Assert.Null(window.Controller.Notice);
        Assert.Null(first.Notice);
        Assert.True(window.Controller.Settings.HistoryEnabled);

        // Reopening uses the same persisted SQLite settings, not the in-memory controller.
        await fixture.Restart();
        window = fixture.Window;
        await window.Controller.StartAsync();
        Settle(window);
        Assert.Null(window.Controller.Notice);
        Assert.True(await window.Controller.SendAsync("persistedhistoryprobe"));
        window.Controller.FlushOutput();
        await window.Controller.FlushHistoryAsync();
        Assert.NotEmpty(await Task.Run(() => fixture.History.Search("persistedhistoryprobe", new())));

        window.Controller.ShowNotice(L.HistoryRecordingFailed);
        Settle(window);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == L.HistoryRecordingFailed);
        Assert.False(window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "HideHistoryRecordingNotice").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task CloseOnlyDismissesCurrentReminderAndOtherNoticesCannotBeSuppressed()
    {
        await using var fixture = new Fixture();
        var window = fixture.Window;
        await window.Controller.StartAsync();
        Settle(window);
        var dismiss = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "DismissNotice");
        Assert.NotNull(dismiss);
        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(window.Controller.Notice);
        await window.Controller.DisconnectAsync();
        await window.Controller.StartAsync();
        Assert.Equal(L.HistoryRecordingNotice, window.Controller.Notice);
        window.Controller.ShowNotice("A different notice");
        Settle(window);
        Assert.False(window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "HideHistoryRecordingNotice").IsEffectivelyVisible);
    }

    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "wandur-history-notice-" + Guid.NewGuid());
        private readonly ClientDatabase _database;
        public FallibleSettings Settings { get; }
        public SqliteHistoryStore History { get; }
        public MainWindow Window { get; private set; }

        public Fixture()
        {
            _database = new ClientDatabase(Path.Combine(_directory, "test.db"));
            Settings = new(new SqliteSettingsStore(_database, Path.Combine(_directory, "legacy.json")));
            History = new(_database);
            Window = Create();
        }

        private MainWindow Create()
        {
            var window = new MainWindow(new TranscriptDisplayFactory(), Settings, new MemoryPasswordVault(),
                new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore(), history: History);
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

    private sealed class FallibleSettings(ISettingsStore inner) : ISettingsStore
    {
        public bool FailSaves { get; set; }
        public string FilePath => inner.FilePath;
        public SettingsLoadResult Load() => inner.Load();
        public void Save(ClientSettings settings)
        {
            if (FailSaves) throw new IOException("Simulated write failure");
            inner.Save(settings);
        }
    }
}
