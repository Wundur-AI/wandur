using L = Wandur.Core.Localization.Strings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;

namespace Wandur.Desktop.Views;

public sealed partial class WorldBrowserView
{
    private const string Magnifier = "M 7,1.5 A 5.5,5.5 0 1 1 6.99,1.5 Z M 11,11 L 15,15";
    private readonly List<(ComboBox Input, string Key)> _facets = [];
    private readonly ComboBox _sort = LocalizedChoice("DirectorySort", nameof(L.BestMatch), nameof(L.NameAZ), nameof(L.LastObservedPlayers), nameof(L.HighestRated), nameof(L.RecentlyUpdated), nameof(L.NewestWorlds));
    private readonly ComboBox _rating = LocalizedChoice("DirectoryRatingFilter", nameof(L.AnyRating), nameof(L.Label3Stars), nameof(L.Label4Stars), nameof(L.Label45Stars));
    private readonly NumericUpDown _minimumPlayers = PlayerNumber("DirectoryMinimumPlayers");
    private readonly NumericUpDown _maximumPlayers = PlayerNumber("DirectoryMaximumPlayers");
    private readonly CheckBox _tlsFilter = new() { Name = "DirectoryTlsFilter", [!ContentControl.ContentProperty] = LocalizedText.Binding(nameof(L.TLSAvailable)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _adultFilter = new() { Name = "DirectoryAdultFilter", [!ContentControl.ContentProperty] = LocalizedText.Binding(nameof(L.ShowAdultWorlds)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _filterSummary = Ui.TextKey(nameof(L.AdvancedSearch), 13);
    private readonly TextBlock _filterHint = Ui.TextKey(nameof(L.AllPreferencesCombinePlayerCountsAreLastObservedNot), 11, "muted");
    private bool _ready;
    private bool _updatingFilters;
    private Button _resetFilters = null!;

    private static ComboBox Choice(string name, params string[] items) => new()
    { Name = name, ItemsSource = items, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private static ComboBox LocalizedChoice(string name, params string[] keys)
    {
        var choice = Choice(name, keys);
        // The template reads the box's own size, so the bar's selects can be the site's 15 and the filter popup's 12.
        choice.ItemTemplate = new FuncDataTemplate<string>((key, _) =>
        {
            if (key is null) return null;
            var text = Ui.TextKey(key, 12);
            text.Bind(TextBlock.FontSizeProperty, choice.GetObservable(TemplatedControl.FontSizeProperty));
            return text;
        });
        return choice;
    }
    private static NumericUpDown PlayerNumber(string name) => new()
    { Name = name, Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", [!NumericUpDown.PlaceholderTextProperty] = LocalizedText.Binding(nameof(L.Any)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };

    /// <summary>The site's field: a 48 high rounded box on the card surface, the control inside it borderless.</summary>
    private static Border Field(string name, params Control[] parts)
    {
        var row = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
        for (var i = 0; i < parts.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(i == parts.Length - 1 ? GridLength.Star : GridLength.Auto));
            Grid.SetColumn(parts[i], i); parts[i].VerticalAlignment = VerticalAlignment.Center; row.Children.Add(parts[i]);
        }
        var field = new Border
        {
            Name = name, Classes = { "directory-field" }, Height = 46, Padding = new Thickness(14, 0, 8, 0),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = row
        };
        field.Paint(Border.BackgroundProperty, "PanelBrush").Paint(Border.BorderBrushProperty, "LineBrush");
        return field;
    }

    private static void Borderless(TemplatedControl control, double size = 15)
    {
        control.Background = Brushes.Transparent; control.BorderThickness = new Thickness(0);
        control.FontSize = size; control.Padding = new Thickness(0); control.MinHeight = 0;
    }

    private Control CreateFilterBar()
    {
        // Search, the online select and the sort select, as the site's bar; the client's further filters open
        // from the Filters button in a bounded popup.
        var magnifier = new Path { Data = StreamGeometry.Parse(Magnifier), Width = 17, Height = 17, Stretch = Stretch.Uniform, StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round };
        magnifier.Paint(Shape.StrokeProperty, "MutedBrush");
        Borderless(_search);
        var search = Field("DirectorySearchField", magnifier, _search);
        search.Padding = new Thickness(16, 0, 10, 0);

        foreach (var choice in new[] { _onlineChoice, _sort }) { Borderless(choice); choice.HorizontalAlignment = HorizontalAlignment.Stretch; }
        var onlineDot = DirectoryLook.Dot();
        _onlineChoice.SelectionChanged += (_, _) => onlineDot.Paint(Shape.FillProperty, _onlineChoice.SelectedIndex == 1 ? "LiveBrush" : "MutedBrush");
        onlineDot.Paint(Shape.FillProperty, "MutedBrush");
        var online = Field("DirectoryOnlineField", onlineDot, _onlineChoice);
        online.MinWidth = 170;
        var sortLabel = Ui.TextKey(nameof(L.SortLabel), 15, "muted");
        var sort = Field("DirectorySortField", sortLabel, _sort);
        sort.MinWidth = 210;

        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(string title, Control input)
        {
            var field = Ui.FieldKey(title, input);
            field.Width = 170; field.Margin = new Thickness(0, 0, 12, 12); fields.Children.Add(field);
        }
        foreach (var facet in _model.Facets)
        {
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
        _adultFilter.Bind(ToolTip.TipProperty, LocalizedText.Binding(nameof(L.AdultWorldsHint)));
        _adultFilter.IsCheckedChanged += (_, _) => Filter();
        Add(nameof(L.AdultWorlds), _adultFilter);
        _minimumPlayers.ValueChanged += (_, _) => Filter();
        _maximumPlayers.ValueChanged += (_, _) => Filter();
        _rating.SelectionChanged += (_, _) => Filter();
        _tlsFilter.IsCheckedChanged += (_, _) => Filter();
        _resetFilters = DirectoryLook.Link(L.ClearSearchFilters, () => _model.ResetFiltersCommand.Execute(null), 13, weight: FontWeight.Normal);
        _resetFilters.Name = "DirectoryResetFilters";
        _resetFilters.Bind(ContentControl.ContentProperty, LocalizedText.Binding(nameof(L.ClearSearchFilters)));
        var resetExpanded = Ui.ButtonKey(nameof(L.ClearSearchFilters), () => _model.ResetFiltersCommand.Execute(null), "quiet");
        resetExpanded.Name = "DirectoryResetAdvancedFilters";
        Add(nameof(L.StartAgain), resetExpanded);
        var content = Ui.Stack(_filterSummary, fields, _filterHint);
        var scroll = new ScrollViewer { Name = "DirectoryAdvancedSearch", Content = content, MaxHeight = 320,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var flyout = new Flyout { Content = scroll };
        flyout.Opening += (_, _) => { content.Width = Math.Clamp(Bounds.Width - 70, 190, 550); scroll.MaxHeight = Math.Clamp(Bounds.Height - 90, 150, 360); };
        var filter = Ui.ButtonKey(nameof(L.DirectoryFilters), () => { });
        filter.Name = "DirectoryFiltersButton"; filter.FontSize = 15; filter.Flyout = flyout;
        filter.Height = 46; filter.Padding = new Thickness(16, 0); filter.VerticalContentAlignment = VerticalAlignment.Center;
        filter.CornerRadius = new CornerRadius(10);

        var layout = new Grid { ColumnSpacing = 12, RowSpacing = 10, Children = { search, online, sort, filter } };
        void Place(bool narrow)
        {
            // Narrow: search and Filters on one line, the two selects sharing the next, as the site stacks its bar.
            layout.ColumnDefinitions = narrow ? new ColumnDefinitions("*,*,Auto") : new ColumnDefinitions("*,Auto,Auto,Auto");
            layout.RowDefinitions = narrow ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
            Grid.SetRow(search, 0); Grid.SetColumn(search, 0); Grid.SetColumnSpan(search, narrow ? 2 : 1);
            Grid.SetRow(filter, 0); Grid.SetColumn(filter, narrow ? 2 : 3);
            Grid.SetRow(online, narrow ? 1 : 0); Grid.SetColumn(online, narrow ? 0 : 1);
            Grid.SetRow(sort, narrow ? 1 : 0); Grid.SetColumn(sort, narrow ? 1 : 2); Grid.SetColumnSpan(sort, narrow ? 2 : 1);
            online.MinWidth = narrow ? 0 : 170; sort.MinWidth = narrow ? 0 : 210;
            sortLabel.IsVisible = !narrow;
        }
        bool? placed = null;
        layout.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 700;
            if (placed == narrow) return;
            placed = narrow; Place(narrow);
        };
        Place(false);
        return layout;
    }

    private void RefreshFilterOptions()
    {
        _updatingFilters = true;
        foreach (var (input, key) in _facets)
        {
            var selected = _model.Query.Facets.GetValueOrDefault(key);
            input.ItemsSource = new[] { L.Any }.Concat(_model.FacetOptions.GetValueOrDefault(key) ?? []).ToArray();
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
        _onlineChoice.SelectedIndex = query.OnlineOnly ? 1 : 0; _sort.SelectedIndex = query.Sort;
        foreach (var (input, key) in _facets)
        {
            input.SelectedIndex = 0;
            if (query.Facets.TryGetValue(key, out var selected)) input.SelectedItem = selected;
        }
        _minimumPlayers.Value = query.MinimumPlayers; _maximumPlayers.Value = query.MaximumPlayers;
        _rating.SelectedIndex = query.Rating; _tlsFilter.IsChecked = query.TlsOnly; _adultFilter.IsChecked = query.ShowAdult;
        _updatingFilters = false;
    }
}
