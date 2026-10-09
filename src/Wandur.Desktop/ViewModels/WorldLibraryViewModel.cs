using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wandur.Core.Settings;
using Wandur.Core.Storage;

namespace Wandur.Desktop.ViewModels;

public sealed partial class WorldLibraryViewModel : ObservableObject
{
    private readonly SessionWorkspace _sessions;
    private readonly Action<ConnectionProfile>? _editProfile;
    /// <summary>The saved list the shown order was built from, in the manual order the settings keep.</summary>
    private IReadOnlyList<ConnectionProfile>? _source;
    /// <summary>Every saved world in the order shown, before the filter.</summary>
    private IReadOnlyList<ConnectionProfile> _ordered = [];
    [ObservableProperty] private IReadOnlyList<ConnectionProfile> _profiles = [];
    [ObservableProperty] private ConnectionProfile? _selectedProfile;
    /// <summary>The Find action's box is open over the list.</summary>
    [ObservableProperty] private bool _isFilterVisible;
    /// <summary>Words the shown worlds' names or addresses must contain, ignoring case.</summary>
    [ObservableProperty] private string _filter = "";
    public bool HasWorlds => _ordered.Count > 0;
    public bool IsEmpty => !HasWorlds;
    /// <summary>There are saved worlds, but the filter hides them all.</summary>
    public bool HasNoMatches => HasWorlds && Profiles.Count == 0;
    public bool CanBrowse { get; }
    public IRelayCommand AddCommand { get; }
    public IRelayCommand BrowseCommand { get; }
    /// <summary>Set by the view: true while the pointer is over the list or a row's menu is open, when a reorder would jump under the user.</summary>
    internal Func<bool>? IsBusy { get; set; }
    /// <summary>A usage change arrived while the list was busy; the order is applied when the pointer leaves or the panel next opens.</summary>
    public bool ReorderPending { get; private set; }

    public WorldLibraryViewModel(SessionWorkspace sessions, Action addWorld, Action? browseWorlds, Action<ConnectionProfile>? editProfile)
    {
        _sessions = sessions; _editProfile = editProfile; CanBrowse = browseWorlds is not null;
        AddCommand = new RelayCommand(addWorld);
        BrowseCommand = new RelayCommand(() => browseWorlds?.Invoke());
        Refresh();
    }

    public void Attach()
    {
        _sessions.Changed += Refresh;
        _sessions.UsageChanged += UsageChanged;
        // Connections counted while the panel was closed are applied as it opens: nobody is looking yet.
        _source = null; ReorderPending = false;
        Refresh();
    }
    public void Detach() { _sessions.Changed -= Refresh; _sessions.UsageChanged -= UsageChanged; }

    private void Refresh()
    {
        var source = _sessions.Active.Controller.Settings.Profiles;
        if (ReferenceEquals(source, _source)) return;
        // Every tab loads the settings for itself, so a new list with the same worlds in the same order is
        // not a change: only an edit, an addition or a removal reorders the list at once.
        var same = _source is not null && source.SequenceEqual(_source);
        _source = source;
        if (same) return;
        ReorderPending = false;
        Show(Order(source));
    }

    /// <summary>
    /// The reorder rule: a connection just counted moves its world up at once unless the pointer is over the
    /// list or a row menu is open, in which case the new order waits for the pointer to leave.
    /// </summary>
    private void UsageChanged()
    {
        if (_source is null) return;
        if (IsBusy?.Invoke() == true) { ReorderPending = true; return; }
        ReorderPending = false;
        Show(Order(_source));
    }

    public void ApplyPendingOrder()
    {
        if (!ReorderPending || _source is null) return;
        ReorderPending = false;
        Show(Order(_source));
    }

    /// <summary>Most used first; a store that cannot be read leaves the manual order, which is also what a client without a database shows.</summary>
    private IReadOnlyList<ConnectionProfile> Order(IReadOnlyList<ConnectionProfile> source)
    {
        if (_sessions.Usage is not { } store || source.Count < 2) return source;
        try { return WorldUsage.Order(source, store.Load(), DateTimeOffset.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException) { return source; }
    }

    private void Show(IReadOnlyList<ConnectionProfile> ordered)
    {
        _ordered = ordered;
        var id = SelectedProfile?.Id;
        var shown = Matching(ordered);
        if (!Profiles.SequenceEqual(shown)) Profiles = shown;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasWorlds)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasNoMatches));
    }

    private IReadOnlyList<ConnectionProfile> Matching(IReadOnlyList<ConnectionProfile> ordered)
    {
        var words = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return ordered;
        return ordered.Where(p => words.All(w => p.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
            || $"{p.Host}:{p.Port}".Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    partial void OnFilterChanged(string value) => Show(_ordered);
    // Closing the box clears what it filtered by, so the whole list comes back.
    partial void OnIsFilterVisibleChanged(bool value) { if (!value) Filter = ""; }
    // Selecting a saved world only chooses what Connect will open; its theme arrives with the session.
    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        EditCommand.NotifyCanExecuteChanged(); ConnectCommand.NotifyCanExecuteChanged();
        RequestDeleteCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
    }
    private bool CanEdit() => _editProfile is not null && SelectedProfile is not null;
    private bool CanConnect() => SelectedProfile is not null;
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit() { if (SelectedProfile is { } profile) _editProfile?.Invoke(profile); }
    [RelayCommand]
    private void EditProfile(ConnectionProfile? profile) { if (profile is null) return; SelectedProfile = profile; Edit(); }
    /// <summary>
    /// Connect goes to the world's session when one is already open, so a double-click never logs the same character
    /// in twice by accident; Connect in new tab always opens another session.
    /// </summary>
    // A connection can take a while to finish; another world can be opened meanwhile, each in its own session.
    [RelayCommand(CanExecute = nameof(CanConnect), AllowConcurrentExecutions = true)]
    private Task ConnectAsync() => SelectedProfile is { } profile ? ConnectProfileAsync(profile) : Task.CompletedTask;
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ConnectProfileAsync(ConnectionProfile? profile)
    {
        if (profile is null) return;
        SelectedProfile = profile;
        if (_sessions.SessionOf(profile) is { } open) _sessions.Select(open);
        else await _sessions.OpenAsync(profile);
    }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ConnectInNewTabAsync(ConnectionProfile? profile)
    {
        if (profile is null) return;
        SelectedProfile = profile;
        await _sessions.OpenAsync(profile);
    }

    /// <summary>
    /// A copy of a saved world under a new name, with its settings, login and automation, selected so it can be edited.
    /// The saved password stays with the original: a copy is usually for another character.
    /// </summary>
    [RelayCommand]
    private void DuplicateProfile(ConnectionProfile? profile)
    {
        var controller = _sessions.Active.Controller;
        if (profile is null || controller.Settings.Profiles.FirstOrDefault(p => p.Id == profile.Id) is not { } original) return;
        var name = Wandur.Core.Localization.Strings.Format(Wandur.Core.Localization.Strings.ScriptDuplicateName, original.Name);
        // A world's name is at most 100 characters; a long one gives up its end to the copy marker.
        if (name.Length > 100) name = Wandur.Core.Localization.Strings.Format(Wandur.Core.Localization.Strings.ScriptDuplicateName, original.Name[..Math.Max(1, original.Name.Length - (name.Length - 100))]);
        var copy = original with { Id = Guid.NewGuid(), Name = name, PasswordId = null, AutoLogin = false };
        var profiles = controller.Settings.Profiles.ToList();
        profiles.Insert(profiles.FindIndex(p => p.Id == original.Id) + 1, copy);
        try { controller.SaveSettings(controller.Settings with { Profiles = profiles }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { controller.ShowNotice(ex.Message); return; }
        Refresh();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == copy.Id) ?? SelectedProfile;
    }

    /// <summary>True when the directory lists this world, so it has a page to explore.</summary>
    public bool CanExplore(ConnectionProfile profile) => _sessions.ListingFor(profile) is not null;
    [RelayCommand]
    private void ExploreProfile(ConnectionProfile? profile) { if (profile is not null) _sessions.Explore(profile); }
    /// <summary>
    /// The world pending deletion, or null when nothing is. Removing a saved world throws away its
    /// credentials, its protocol mapping and its scripts, and the button sits in a toolbar beside the ones
    /// that add and edit, so a single stray click used to be enough to lose all of it with no way back.
    /// Same two-step the script and macro libraries use.
    /// </summary>
    [ObservableProperty] private ConnectionProfile? _pendingDelete;
    public bool ConfirmDelete => PendingDelete is not null;
    /// <summary>
    /// The name in the prompt. The prompt and the deletion both read the pending world rather than the
    /// selection, so they cannot disagree even when a row is right-clicked and the selection then moves.
    /// </summary>
    public string PendingDeleteName => PendingDelete?.Name ?? "";

    partial void OnPendingDeleteChanged(ConnectionProfile? value)
    {
        OnPropertyChanged(nameof(ConfirmDelete));
        OnPropertyChanged(nameof(PendingDeleteName));
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void RequestDelete() => PendingDelete = SelectedProfile;
    [RelayCommand]
    private void RequestDeleteProfile(ConnectionProfile? profile) => PendingDelete = profile;
    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    private bool CanDelete() => PendingDelete is not null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var clicked = PendingDelete;
        PendingDelete = null;
        var controller = _sessions.Active.Controller;
        var profile = controller.Settings.Profiles.FirstOrDefault(p => p.Id == clicked?.Id);
        if (profile is null) return;
        try { await controller.RemoveWorldAsync(profile); Refresh(); }
        catch (Exception ex) { controller.ShowNotice(ex.Message); }
    }
}
