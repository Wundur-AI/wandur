using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Wandur.Core.Updates;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop;

/// <summary>
/// Update checks. Shortly after the window opens, and then at most once a day while it runs, the client asks
/// wandur.net for the newest release (<see cref="UpdateService"/>); when it is newer than this build, a strip in the
/// session notice's own style says so, with Download (the downloads page in the browser), Release notes and Skip this
/// version. It is hidden while the active session takes private input (including a password prompt), its buttons never
/// take keyboard focus, and nothing is ever downloaded or installed. Automatic checks fail silently and never retry in
/// a loop; Check for Updates asks at once and says the result.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How long after the window opens the first automatic check waits, so it never competes with startup or a first connection.</summary>
    internal static TimeSpan UpdateStartupDelay { get; set; } = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateTick = TimeSpan.FromHours(1);

    private UpdateService _updates = null!;
    private IDisposable? _updateSource;
    private Border _updateNotice = null!;
    private TextBlock _updateText = null!;
    private Button _updateNotes = null!;
    private DispatcherTimer? _updateTimer;
    private bool _updateChecking;
    private bool _updateDismissed;

    /// <summary>The release the strip offers, or null.</summary>
    internal UpdateInfo? OfferedUpdate { get; private set; }
    internal UpdateService Updates => _updates;
    /// <summary>Whether the update strip is showing right now.</summary>
    internal bool IsUpdateNoticeVisible => _updateNotice.IsVisible;

    private Control CreateUpdateNotice(UpdateService? updates)
    {
        if (updates is null)
        {
            var source = new HttpUpdateSource(Catalog.BaseUri);
            _updateSource = source;
            updates = UpdateService.ForThisBuild(source);
        }
        _updates = updates;

        _updateText = Ui.Text("", 12);
        _updateText.Name = "UpdateNoticeText";
        _updateText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _updateText.VerticalAlignment = VerticalAlignment.Center;
        _updateText.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("TextBrush"));
        var download = NoticeButton("UpdateDownload", nameof(L.UpdateDownload), () => OpenUpdatePage(OfferedUpdate?.Page), "primary");
        _updateNotes = NoticeButton("UpdateReleaseNotes", nameof(L.UpdateReleaseNotes), () => OpenUpdatePage(OfferedUpdate?.Notes ?? OfferedUpdate?.Page), null);
        var skip = NoticeButton("UpdateSkip", nameof(L.UpdateSkipVersion), SkipOfferedUpdate, "quiet");
        var dismiss = NoticeButton("DismissUpdate", "", () => { _updateDismissed = true; RefreshUpdateNotice(); }, "quiet");
        dismiss.Content = "×";
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { download, _updateNotes, skip, dismiss } };
        Grid.SetColumn(actions, 1);
        _updateNotice = new Border
        {
            Name = "UpdateNotice", Padding = new Thickness(18, 7), IsVisible = false,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _updateText, actions } }
        };
        _updateNotice.Bind(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("PanelBrush"));
        _updateNotice.Bind(Border.BorderBrushProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("LineBrush"));

        // What the last check learned shows at once; the network is asked only once the startup delay has passed.
        OfferedUpdate = _updates.Offer(Controller.Settings);
        Opened += (_, _) =>
        {
            if (!_updates.ChecksAllowed) return;
            _updateTimer = new DispatcherTimer(UpdateStartupDelay, DispatcherPriority.Background, async (_, _) =>
            {
                if (_updateTimer is { } timer) timer.Interval = UpdateTick;
                await AutomaticUpdateCheckAsync();
            });
            _updateTimer.Start();
        };
        Closed += (_, _) => { _updateTimer?.Stop(); _updateSource?.Dispose(); };
        return _updateNotice;
    }

    /// <summary>A notice button that never takes keyboard focus, so a click leaves the command box focused.</summary>
    private static Button NoticeButton(string name, string key, Action action, string? cssClass)
    {
        var button = key.Length > 0 ? Ui.ButtonKey(key, action, cssClass) : Ui.Button("", action, cssClass);
        button.Name = name;
        button.Focusable = false;
        button.IsTabStop = false;
        button.FontSize = 12;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    /// <summary>The scheduled check: only when one is due (a day since the last), silent on failure.</summary>
    internal async Task AutomaticUpdateCheckAsync()
    {
        if (_closing || _updateChecking || !_updates.IsDue(Controller.Settings)) return;
        var result = await RunUpdateCheckAsync();
        if (result?.Status == UpdateCheckStatus.Available) OfferedUpdate = _updates.Offer(Controller.Settings);
        RefreshUpdateNotice();
    }

    /// <summary>Check for Updates: asks now, whatever the schedule, and returns what to say. A newer release shows the
    /// notice (and returns null: the notice is the answer); otherwise the sentence for a dialog.</summary>
    internal async Task<(UpdateCheckStatus Status, string? Heading, string Message)> CheckForUpdatesNowAsync()
    {
        if (!_updates.ChecksAllowed)
            return (UpdateCheckStatus.Disabled, L.Format(L.UpdateChecksOffInSourceBuild, _updates.RunningVersion), "");
        var result = await RunUpdateCheckAsync();
        switch (result?.Status)
        {
            case UpdateCheckStatus.Available when result.Latest is { } latest:
                OfferedUpdate = latest;
                _updateDismissed = false;
                RefreshUpdateNotice();
                return (UpdateCheckStatus.Available, null, "");
            case UpdateCheckStatus.UpToDate:
                return (UpdateCheckStatus.UpToDate, L.Format(L.UpdateUpToDate, _updates.RunningVersion), "");
            default:
                return (UpdateCheckStatus.Failed, L.UpdateUnreachable, L.UpdateUnreachableHint);
        }
    }

    /// <summary>The menu command: the check, then a dialog unless the notice already answers.</summary>
    internal async Task CheckForUpdatesFromMenuAsync()
    {
        var (_, heading, message) = await CheckForUpdatesNowAsync();
        if (heading is not null) await ShowInformationAsync(L.CheckForUpdatesTitle, heading, message);
    }

    private async Task<UpdateCheckResult?> RunUpdateCheckAsync()
    {
        if (_updateChecking) return null;
        _updateChecking = true;
        try
        {
            var result = await _updates.CheckAsync(Controller.Settings);
            if (result.Record is { } record && !_closing)
            {
                try { Controller.SaveSettings(Controller.Settings with { LastUpdateCheck = record }); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Data.Common.DbException or ArgumentException) { }
            }
            return result;
        }
        finally { _updateChecking = false; }
    }

    private void SkipOfferedUpdate()
    {
        if (OfferedUpdate is not { } offered) return;
        try { Controller.SaveSettings(Controller.Settings with { SkippedUpdateVersion = offered.Version }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Data.Common.DbException or ArgumentException) { }
        OfferedUpdate = null;
        RefreshUpdateNotice();
    }

    private async void OpenUpdatePage(Uri? page)
    {
        if (page is null) return;
        try { if (!await Launcher.LaunchUriAsync(page)) Controller.ShowNotice(L.LinkNotOpened); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { Controller.ShowNotice(L.LinkNotOpened); }
    }

    /// <summary>Shown when a release is offered and not dismissed, and never while the active session's input is private.</summary>
    private void RefreshUpdateNotice()
    {
        if (_updateNotice is null) return;
        _updateNotice.IsVisible = OfferedUpdate is not null && !_updateDismissed && !Controller.IsPrivate;
        if (OfferedUpdate is { } shown)
        {
            _updateText.Text = L.Format(L.UpdateAvailable, shown.Version);
            _updateNotes.IsVisible = shown.Notes is not null;
        }
    }
}
