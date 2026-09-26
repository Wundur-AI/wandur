using L = Wandur.Core.Localization.Strings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Wandur.Desktop.Views;

public sealed partial class WorldBrowserView
{
    private readonly List<(ComboBox Input, string Key)> _facets = [];
    private readonly ComboBox _sort = LocalizedChoice("DirectorySort", nameof(L.BestMatch), nameof(L.NameAZ), nameof(L.LastObservedPlayers), nameof(L.HighestRated), nameof(L.RecentlyUpdated), nameof(L.NewestWorlds));
    private readonly ComboBox _rating = LocalizedChoice("DirectoryRatingFilter", nameof(L.AnyRating), nameof(L.Label3Stars), nameof(L.Label4Stars), nameof(L.Label45Stars));
    private readonly NumericUpDown _minimumPlayers = PlayerNumber("DirectoryMinimumPlayers");
    private readonly NumericUpDown _maximumPlayers = PlayerNumber("DirectoryMaximumPlayers");
    private readonly CheckBox _tlsFilter = new() { Name = "DirectoryTlsFilter", [!ContentControl.ContentProperty] = LocalizedText.Binding(nameof(L.TLSAvailable)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _filterSummary = Ui.TextKey(nameof(L.AdvancedSearch), 13);
    private readonly TextBlock _filterHint = Ui.TextKey(nameof(L.AllPreferencesCombinePlayerCountsAreLastObservedNot), 11, "muted");
    private bool _ready;
    private bool _updatingFilters;
    private readonly ComboBox _genre = Choice("DirectoryThemeFilter");
    private Button _resetFilters = null!;

    private static ComboBox Choice(string name, params string[] items) => new()
    { Name = name, ItemsSource = items, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private static ComboBox LocalizedChoice(string name, params string[] keys)
    {
        var choice = Choice(name, keys);
        choice.ItemTemplate = new FuncDataTemplate<string>((key, _) => key is null ? null : Ui.TextKey(key, 12));
        return choice;
    }
    private static NumericUpDown PlayerNumber(string name) => new()
    { Name = name, Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", [!NumericUpDown.PlaceholderTextProperty] = LocalizedText.Binding(nameof(L.Any)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };

    private Control CreateFilterBar()
    {
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(string title, Control input)
        {
            var field = Ui.FieldKey(title, input);
            field.Width = 170; field.Margin = new Thickness(0, 0, 12, 12); fields.Children.Add(field);
        }
        _facets.Add((_genre, "Theme"));
        _genre.SelectionChanged += (_, _) => Filter();
        _genre.MaxWidth = 220;
        _genre.HorizontalAlignment = HorizontalAlignment.Left;
        _genre.Width = 180;
        _genre.Bind(Avalonia.Automation.AutomationProperties.NameProperty, LocalizedText.Binding(nameof(L.AllGenres)));
        foreach (var facet in _model.Facets)
        {
            if (facet.Key == "Theme") continue;
            var input = Choice("Directory" + facet.Key + "Filter", L.Any);
            _facets.Add((input, facet.Key));
            input.SelectionChanged += (_, _) => Filter();
            Add(facet.LabelKey, input);
        }
        Add(nameof(L.MinObservedPlayers), _minimumPlayers);
        Add(nameof(L.MaxObservedPlayers), _maximumPlayers);
        Add(nameof(L.MinimumRating), _rating);
        Add(nameof(L.ConnectionSecurity), _tlsFilter);
        Add(nameof(L.MUDConnections), _connectionFilter);
        Add(nameof(L.ReportedOnline), _onlineFilter);
        _minimumPlayers.ValueChanged += (_, _) => Filter();
        _maximumPlayers.ValueChanged += (_, _) => Filter();
        _rating.SelectionChanged += (_, _) => Filter();
        _tlsFilter.IsCheckedChanged += (_, _) => Filter();
        _resetFilters = Ui.ButtonKey(nameof(L.ClearSearchFilters), () => _model.ResetFiltersCommand.Execute(null), "quiet");
        _resetFilters.Name = "DirectoryResetFilters"; _resetFilters.FontSize = 11;
        _resetFilters.MinHeight = 24; _resetFilters.Padding = new Thickness(6, 2);
        var resetExpanded = Ui.ButtonKey(nameof(L.ClearSearchFilters), () => _model.ResetFiltersCommand.Execute(null), "quiet");
        resetExpanded.Name = "DirectoryResetAdvancedFilters";
        Add(nameof(L.StartAgain), resetExpanded);
        var content = Ui.Stack(_filterSummary, fields, _filterHint);
        var scroll = new ScrollViewer { Name = "DirectoryAdvancedSearch", Content = content, MaxHeight = 320,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var flyout = new Flyout { Content = scroll };
        flyout.Opening += (_, _) => { content.Width = Math.Clamp(Bounds.Width - 70, 190, 550); scroll.MaxHeight = Math.Clamp(Bounds.Height - 90, 150, 360); };
        var filter = Ui.ButtonKey(nameof(L.DirectoryFilters), () => { });
        filter.Name = "DirectoryFiltersButton"; filter.FontSize = 12; filter.Flyout = flyout;
        var sortMenu = new MenuFlyout();
        for (var index = 0; index < _sort.ItemCount; index++)
        {
            var sortIndex = index;
            var item = new MenuItem();
            item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LocalizedText.Binding((string)_sort.Items[index]!));
            item.Click += (_, _) => _sort.SelectedIndex = sortIndex;
            sortMenu.Items.Add(item);
        }
        var sortButton = Ui.ToolbarIconKey(new Button { Name = "DirectoryCompactSort", Flyout = sortMenu },
            "M 4,1 V 15 M 1,4 L 4,1 L 7,4 M 12,1 V 15 M 9,12 L 12,15 L 15,12", nameof(L.SortWorldsBy));
        _sort.Width = 155;
        var sorting = new Panel { Children = { _sort, sortButton } };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Children = { _genre, filter, sorting } };
        Grid.SetColumn(filter, 1); Grid.SetColumn(sorting, 2);
        layout.SizeChanged += (_, _) => { _sort.IsVisible = layout.Bounds.Width >= 620; sortButton.IsVisible = !_sort.IsVisible; };
        return layout;
    }

    private void RefreshFilterOptions()
    {
        _updatingFilters = true;
        foreach (var (input, key) in _facets)
        {
            var selected = _model.Query.Facets.GetValueOrDefault(key);
            input.ItemsSource = new[] { key == "Theme" ? L.AllGenres : L.Any }.Concat(_model.FacetOptions.GetValueOrDefault(key) ?? []).ToArray();
            input.SelectedIndex = 0;
            if (selected is not null) input.SelectedItem = selected;
        }
        _updatingFilters = false;
    }

    private void ApplyQueryToControls()
    {
        _updatingFilters = true;
        var query = _model.Query;
        _search.Text = query.Search; _connectionFilter.SelectedIndex = query.Connection;
        _onlineFilter.IsChecked = query.OnlineOnly; _sort.SelectedIndex = query.Sort;
        foreach (var (input, key) in _facets)
        {
            input.SelectedIndex = 0;
            if (query.Facets.TryGetValue(key, out var selected)) input.SelectedItem = selected;
        }
        _minimumPlayers.Value = query.MinimumPlayers; _maximumPlayers.Value = query.MaximumPlayers;
        _rating.SelectedIndex = query.Rating; _tlsFilter.IsChecked = query.TlsOnly;
        _updatingFilters = false;
    }
}
