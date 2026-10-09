using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Wandur.Desktop.ViewModels;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Views;

public sealed class WorkspaceNavigationView : UserControl
{
    private readonly WorkspaceNavigationViewModel _model;
    public WorkspaceNavigationView(WorkspaceNavigationViewModel model)
    {
        _model = model; DataContext = model;
        var list = new ListBox { Name = "WorkspaceItems", Background = Brushes.Transparent, Margin = new Thickness(4), MinHeight = 100 };
        list.Classes.Add("world-list");
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.OpenEntries)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(model.Selected)) { Mode = BindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<WorkspaceNavigationEntry>((entry, _) =>
        {
            if (entry is null) return null;
            var title = Ui.Text("", 13);
            title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Bind(TextBlock.TextProperty, new Binding(nameof(entry.Title)));
            var details = Ui.Text("", 11, "muted");
            details.TextWrapping = TextWrapping.NoWrap; details.TextTrimming = TextTrimming.CharacterEllipsis;
            details.Bind(TextBlock.TextProperty, new Binding(nameof(entry.Details)));
            // A small dot beside the state: the live colour while connected, the secondary text colour otherwise.
            var live = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            live.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("MutedBrush"));
            entry.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(entry.IsLive)) PaintLive(); };
            void PaintLive() => live[!Avalonia.Controls.Shapes.Shape.FillProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(entry.IsLive ? "LiveBrush" : "MutedBrush");
            PaintLive();
            var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = entry.HasDetails, Children = { live, details } };
            // New activity in another session is the one thing on this row worth catching the eye: the accent, not grey.
            var status = Ui.Text("", 10);
            status.Bind(TextBlock.TextProperty, new Binding(nameof(entry.Status)));
            status.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentTextBrush"));
            status.FontWeight = FontWeight.SemiBold; status.VerticalAlignment = VerticalAlignment.Center;
            status.Bind(IsVisibleProperty, new Binding(nameof(entry.HasActivity)));
            var close = new Button { Content = "×", Command = entry.CloseCommand, IsVisible = entry.CanClose, Name = "CloseWorkspaceItem", VerticalAlignment = VerticalAlignment.Center };
            close.Classes.Add("tab-close");
            close.Bind(ToolTip.TipProperty, LocalizedText.Binding(nameof(L.CloseWorkspaceItem)));
            close.Bind(Avalonia.Automation.AutomationProperties.NameProperty, LocalizedText.Binding(nameof(L.CloseWorkspaceItem)));
            var text = new StackPanel { Spacing = 3, Children = { title, state } };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6, Margin = new Thickness(entry.IsChild ? 16 : 0, 2, 0, 2), Children = { text, status, close } };
            Grid.SetColumn(status, 1); Grid.SetColumn(close, 2);
            if (entry.HasDetails) row.Bind(ToolTip.TipProperty, new Binding(nameof(entry.Address)));
            return row;
        });
        list.ContainerPrepared += (_, args) =>
        {
            if (args.Container is not ListBoxItem item || item.Content is not WorkspaceNavigationEntry entry) return;
            item.ContextMenu = new ContextMenu { ItemsSource = new[]
            {
                new MenuItem { [!MenuItem.HeaderProperty] = LocalizedText.Binding(nameof(L.OpenWorkspaceItem)), Command = entry.OpenCommand },
                new MenuItem { [!MenuItem.HeaderProperty] = LocalizedText.Binding(nameof(L.RenameSession)), Command = entry.RenameCommand, IsVisible = entry.CanRename },
                new MenuItem { [!MenuItem.HeaderProperty] = LocalizedText.Binding(nameof(L.FloatWorkspaceItem)), Command = entry.FloatCommand, IsVisible = entry.CanFloat },
                new MenuItem { [!MenuItem.HeaderProperty] = LocalizedText.Binding(nameof(L.CloseWorkspaceItem)), Command = entry.CloseCommand, IsVisible = entry.CanClose }
            } };
        };
        list.ContainerClearing += (_, args) => { args.Container.ContextMenu?.Close(); args.Container.ContextMenu = null; };
        var browse = new ToggleButton { Name = "WorkspaceFindMud", Command = model.BrowseCommand, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        browse.Bind(ContentControl.ContentProperty, LocalizedText.Binding(nameof(L.FindAMUD)));
        browse.Classes.Add("workspace-nav");
        browse.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(model.IsSearchSelected)) { Mode = BindingMode.OneWay });
        var heading = Ui.TextKey(nameof(L.OpenSessions), 11, "muted"); heading.Margin = new Thickness(12, 10, 0, 4);
        var top = new StackPanel { Children = { browse, heading } };
        var name = new TextBox { Name = "SessionName", MaxLength = 100 };
        name.Bind(TextBox.TextProperty, new Binding(nameof(model.RenameText)) { Mode = BindingMode.TwoWay });
        name.Bind(TextBox.PlaceholderTextProperty, LocalizedText.Binding(nameof(L.SessionName)));
        var save = new Button { Command = model.SaveNameCommand };
        save.Bind(ContentControl.ContentProperty, LocalizedText.Binding(nameof(L.RenameSession)));
        var cancel = new Button { Command = model.CancelRenameCommand };
        cancel.Bind(ContentControl.ContentProperty, LocalizedText.Binding(nameof(L.Cancel)));
        var rename = new StackPanel { Spacing = 6, Margin = new Thickness(10), Children = { name, new WrapPanel { Children = { save, cancel } } } };
        rename.Bind(IsVisibleProperty, new Binding(nameof(model.IsRenaming)));
        rename.PropertyChanged += (_, args) => { if (args.Property == IsVisibleProperty && rename.IsVisible) Avalonia.Threading.Dispatcher.UIThread.Post(() => { name.Focus(); name.SelectAll(); }); };
        name.KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Enter) { model.SaveNameCommand.Execute(null); args.Handled = true; }
            else if (args.Key == Avalonia.Input.Key.Escape) { model.CancelRenameCommand.Execute(null); args.Handled = true; }
        };
        list.KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.F2 && model.Selected?.CanRename == true) { model.Selected.RenameCommand.Execute(null); args.Handled = true; } };
        // Open sessions sit right under their heading and scroll in the rest of the panel; Saved worlds have a panel
        // of their own under this one.
        list.MinHeight = 0;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Children = { top, list, rename } };
        Grid.SetRow(list, 1); Grid.SetRow(rename, 2); Content = layout;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _model.Attach(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _model.Detach(); base.OnDetachedFromVisualTree(e); }
}
