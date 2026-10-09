using L = Wandur.Core.Localization.Strings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Wandur.Core.Settings;
using Wandur.Desktop.ViewModels;

namespace Wandur.Desktop.Views;

public sealed class WorldLibraryView : UserControl
{
    private readonly WorldLibraryViewModel _model;

    public WorldLibraryView(SessionWorkspace sessions, Action addWorld, Action? browseWorlds = null, Action<ConnectionProfile>? editProfile = null)
    {
        _model = new WorldLibraryViewModel(sessions, addWorld, browseWorlds, editProfile);
        DataContext = _model;
        var worlds = new ListBox { Name = "WorldProfiles", MinHeight = 80, Margin = new Thickness(4, 6), Background = Brushes.Transparent };
        worlds.Classes.Add("world-list");
        worlds.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(_model.Profiles)));
        worlds.Bind(ListBox.SelectedItemProperty, new Binding(nameof(_model.SelectedProfile)) { Mode = BindingMode.TwoWay });
        worlds.Bind(IsVisibleProperty, new Binding(nameof(_model.HasWorlds)));
        worlds.ItemTemplate = new FuncDataTemplate<ConnectionProfile>((profile, _) =>
        {
            if (profile is null) return null;
            var name = Ui.Text(profile.Name, 13);
            name.FontWeight = FontWeight.Medium;
            name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var endpoint = $"{profile.Host}:{profile.Port}" + (profile.UseTls ? " · TLS" : "");
            var details = Ui.Text(endpoint, 10, "muted");
            details.TextWrapping = TextWrapping.NoWrap; details.TextTrimming = TextTrimming.CharacterEllipsis;
            var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { name, details } };
            // The picture sits on the left at a fixed size, so the two text lines keep the row height they had.
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Children = { new WorldThumbnail(profile, sessions.Thumbnails), text } };
            Grid.SetColumn(text, 1);
            // Named, with its address, because a long name is cut short in a narrow panel.
            ToolTip.SetTip(row, L.Format(L.DoubleClickToConnect, profile.Name, endpoint));
            return row;
        });
        // A reorder waits while the pointer is over the list or a row menu is open, and lands when the pointer leaves.
        var menuOpen = false;
        _model.IsBusy = () => worlds.IsPointerOver || menuOpen;
        worlds.PointerExited += (_, _) => { if (!menuOpen) _model.ApplyPendingOrder(); };
        worlds.ContainerPrepared += (_, args) =>
        {
            if (args.Container is not ListBoxItem item || item.Content is not ConnectionProfile profile) return;
            MenuItem Item(string key, string name, System.Windows.Input.ICommand command) =>
                new() { [!MenuItem.HeaderProperty] = LocalizedText.Binding(key), Name = name, Command = command, CommandParameter = profile };
            item.ContextMenu = new ContextMenu
            {
                ItemsSource = new Control[]
                {
                    Item(nameof(L.ConnectSavedWorld), "ConnectWorldMenu", _model.ConnectProfileCommand),
                    Item(nameof(L.ConnectInNewTab), "ConnectNewTabWorldMenu", _model.ConnectInNewTabCommand),
                    new Separator(),
                    Item(nameof(L.Edit2), "EditWorldMenu", _model.EditProfileCommand),
                    Item(nameof(L.ScriptDuplicate), "DuplicateWorldMenu", _model.DuplicateProfileCommand),
                    Item(nameof(L.ExploreInDirectory), "ExploreWorldMenu", _model.ExploreProfileCommand),
                    new Separator(),
                    Item(nameof(L.DeleteSavedWorld), "DeleteWorldMenu", _model.RequestDeleteProfileCommand),
                }
            };
            item.ContextMenu.Opening += (_, _) =>
            {
                menuOpen = true; _model.SelectedProfile = profile;
                // Only a world the directory lists has a page to explore; the directory can change while the app runs.
                if (item.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "ExploreWorldMenu") is { } explore)
                    explore.IsVisible = _model.CanExplore(profile);
            };
            item.ContextMenu.Closed += (_, _) => { menuOpen = false; if (!worlds.IsPointerOver) _model.ApplyPendingOrder(); };
        };
        worlds.ContainerClearing += (_, args) =>
        {
            args.Container.ContextMenu?.Close();
            args.Container.ContextMenu = null;
        };
        worlds.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (args.Source is not Visual source ||
                source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.Content is not ConnectionProfile profile) return;
            var pointer = args.GetCurrentPoint(worlds).Properties;
            if (!pointer.IsLeftButtonPressed && !pointer.IsRightButtonPressed) return;
            _model.SelectedProfile = profile;
        }, RoutingStrategies.Tunnel);
        worlds.DoubleTapped += async (_, args) =>
        {
            // A double-click on a row connects; one on the empty part of the list does nothing.
            if (args.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().Any())
                await _model.ConnectCommand.ExecuteAsync(null);
        };
        // On the way down: a focused row takes Enter for itself (it selects), so the list would never see it on the way up.
        worlds.AddHandler(KeyDownEvent, async (_, args) =>
        {
            if (args.Key == Key.Enter) { args.Handled = true; await _model.ConnectCommand.ExecuteAsync(null); }
            // Delete (Backspace is the delete key on a Mac keyboard) asks first, as the menu does.
            else if (args.Key is Key.Delete or Key.Back && args.KeyModifiers == KeyModifiers.None && _model.RequestDeleteCommand.CanExecute(null))
            { args.Handled = true; _model.RequestDeleteCommand.Execute(null); }
        }, RoutingStrategies.Tunnel);

        // Add and Find are the panel's actions, in its title bar (or in a row of their own outside a dock panel).
        PanelHeader.SetActions(this, [
            new PanelHeaderAction("AddSavedWorld", "M 8,2 V 14 M 2,8 H 14", nameof(L.AddAWorld)) { Command = _model.AddCommand },
            new PanelHeaderAction("FindSavedWorld", "M 6,2.5 A 3.5,3.5 0 1 0 6,9.5 A 3.5,3.5 0 1 0 6,2.5 M 8.5,8.5 L 13,13", nameof(L.SavedWorldsFind))
                { Source = _model, CheckedPath = nameof(_model.IsFilterVisible) },
        ]);
        var actionBar = new PanelActionBar(this) { Margin = new Thickness(6, 4, 6, 0) };
        var filter = new TextBox { Name = "SavedWorldsFilter", FontSize = 12, Margin = new Thickness(8, 6, 8, 0) };
        filter.Bind(TextBox.TextProperty, new Binding(nameof(_model.Filter)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        filter.Bind(TextBox.PlaceholderTextProperty, LocalizedText.Binding(nameof(L.SavedWorldsFilterPlaceholder)));
        filter.Bind(Avalonia.Automation.AutomationProperties.NameProperty, LocalizedText.Binding(nameof(L.SavedWorldsFind)));
        filter.Bind(IsVisibleProperty, new Binding(nameof(_model.IsFilterVisible)));
        filter.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { _model.IsFilterVisible = false; worlds.Focus(); args.Handled = true; }
            else if (args.Key == Key.Down && _model.Profiles.Count > 0) { worlds.ContainerFromItem(_model.SelectedProfile ?? _model.Profiles[0])?.Focus(); args.Handled = true; }
            else if (args.Key == Key.Enter && _model.Profiles.Count > 0) { args.Handled = true; _ = _model.ConnectCommand.ExecuteAsync(null); }
        };
        // Opening Find puts the cursor in its box.
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_model.IsFilterVisible) && _model.IsFilterVisible)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => filter.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        };
        var empty = Ui.TextKey(nameof(L.KeepYourFavoriteWorldsHereAddOneToStart), 12, "muted");
        empty.Name = "SavedWorldsEmpty";
        empty.Margin = new Thickness(14); empty.VerticalAlignment = VerticalAlignment.Top;
        empty.Bind(IsVisibleProperty, new Binding(nameof(_model.IsEmpty)));
        var noMatch = Ui.TextKey(nameof(L.SavedWorldsNoMatch), 12, "muted");
        noMatch.Name = "SavedWorldsNoMatch";
        noMatch.Margin = new Thickness(14); noMatch.VerticalAlignment = VerticalAlignment.Top;
        noMatch.Bind(IsVisibleProperty, new Binding(nameof(_model.HasNoMatches)));
        var content = new Grid { Children = { worlds, empty, noMatch } };

        // The prompt names the world, because the Delete key acts on the selection and the reader
        // cannot otherwise be sure which row it caught.
        var prompt = Ui.TextKey(nameof(L.DeleteSavedWorldPrompt), 12);
        prompt.TextWrapping = TextWrapping.Wrap;
        var named = Ui.Text("", 12); named.FontWeight = FontWeight.SemiBold;
        named.Bind(TextBlock.TextProperty, new Binding(nameof(_model.PendingDeleteName)));
        var confirmDelete = new Button { Name = "ConfirmDeleteWorld", Command = _model.DeleteCommand };
        confirmDelete.Bind(ContentControl.ContentProperty, LocalizedText.Binding(nameof(L.DeleteSavedWorld)));
        var cancelDelete = new Button { Name = "CancelDeleteWorld", Command = _model.CancelDeleteCommand };
        cancelDelete.Bind(ContentControl.ContentProperty, LocalizedText.Binding(nameof(L.Cancel)));
        var confirm = new StackPanel
        {
            Name = "DeleteWorldConfirm",
            Spacing = 6,
            Margin = new Thickness(10, 8),
            Children =
            {
                named, prompt,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { confirmDelete, cancelDelete }
                }
            }
        };
        confirm.Bind(IsVisibleProperty, new Binding(nameof(_model.ConfirmDelete)));

        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), Children = { actionBar, filter, confirm, content } };
        Grid.SetRow(filter, 1); Grid.SetRow(confirm, 2); Grid.SetRow(content, 3); Content = panel;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _model.Attach(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _model.Detach(); base.OnDetachedFromVisualTree(e); }
}
