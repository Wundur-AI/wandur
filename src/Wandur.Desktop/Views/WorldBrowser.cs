using L = Wandur.Core.Localization.Strings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Wandur.Core.Discovery;
using Wandur.Desktop.ViewModels;
using System.ComponentModel;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Wandur.Desktop.Services;

namespace Wandur.Desktop.Views;

/// <summary>
/// The docked world directory, laid out like wandur.net: a search bar with the online and sort selects, a count,
/// then one row per world; Explore opens the world's own page (hero, chips bar, About beside World details, a
/// glimpse inside) in the same dock. Every colour is the active theme's.
/// </summary>
public sealed partial class WorldBrowserView : UserControl
{
    /// <summary>Below this width the world page stacks into one column, as the site does under 800 pixels.</summary>
    internal const double NarrowBelow = 760;
    private readonly WorldCatalog _catalog;
    private readonly WorldBrowserViewModel _model;
    private bool _renderingResults;
    private bool _restoringScroll;
    private int _detailRenderVersion;
    private readonly TextBox _search = new() { Name = "DirectorySearch", [!TextBox.PlaceholderTextProperty] = LocalizedText.Binding(nameof(L.SearchWorldsThemesOrAnAddress)), MaxLength = 150, FontSize = 15 };
    private readonly ListBox _list = new() { Name = "DirectoryResults", Background = Brushes.Transparent };
    private readonly TextBlock _count = DirectoryLook.Label("", 14, "MutedBrush");
    private readonly TextBlock _status = Ui.Text("", 11, "muted");
    private readonly TextBlock _feedback = Ui.Text("", 12, "muted");
    private readonly ComboBox _connectionFilter = LocalizedChoice("DirectoryConnectionFilter", nameof(L.AllWorlds), nameof(L.MUDConnections), nameof(L.BrowserOnlyWorlds));
    private readonly ComboBox _onlineChoice = LocalizedChoice("DirectoryOnlineChoice", nameof(L.AllWorlds), nameof(L.OnlineNow));
    private readonly StackPanel _details = new() { Name = "DirectoryWorldPage", Spacing = 0 };
    private readonly Image _image = new() { Name = "DirectoryArtwork", Stretch = Stretch.UniformToFill, IsVisible = false, IsHitTestVisible = false };
    private readonly Image _banner = new() { Name = "DirectoryBanner", Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly TextBlock _artStatus = Ui.TextKey(nameof(L.LoadingArtwork), 11, "muted");
    private readonly TextBlock _artPlaceholder = DirectoryLook.Label("", 96, "LineBrush", FontWeight.Bold);
    private readonly Border _artFrame;
    // The one place the directory does not follow the theme: the hero is a photograph, so its scrim and the name and
    // tagline over it are fixed, the site's own night ink, and read the same under Hull as under Ember.
    internal static readonly Color HeroScrimColor = Color.FromArgb(0xE6, 0x0C, 0x14, 0x22);
    internal static readonly IBrush HeroTitleInk = new ImmutableSolidColorBrush(Color.Parse("#E7EEF6"));
    internal static readonly IBrush HeroTaglineInk = new ImmutableSolidColorBrush(Color.Parse("#C9D4E0"));
    private readonly Border _heroScrim = new()
    {
        Name = "DirectoryHeroScrim", IsHitTestVisible = false, IsVisible = false,
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.FromArgb(0, 0x0C, 0x14, 0x22), .35), new GradientStop(HeroScrimColor, 1)]
        }
    };
    private TextBlock? _heroTitle;
    private TextBlock? _heroTagline;
    private readonly ScrollViewer _detailScroll;
    private readonly FlowPanel _listingActions = new() { Gap = 14, LineGap = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _listingToolbar;
    private readonly List<Action<bool>> _narrowLayouts = [];
    private bool? _narrow;
    private CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly Button _refresh;
    private readonly Button _back;
    private readonly Grid _resultsPage;
    private readonly Control _detailPage;
    private readonly StackPanel _intro;
    private readonly StackPanel _emptyResults = new() { Spacing = 10, Margin = new Thickness(16) };
    private WorldThumbnails? _rowThumbnails;
    private CancellationTokenSource? _selection;
    private Bitmap? _bitmap;
    private Bitmap? _bannerBitmap;
    private bool _artLoading;
    private bool _closed = true;

    public WorldBrowserView(WorldCatalog catalog, SessionWorkspace sessions)
        : this(new WorldBrowserViewModel(catalog, sessions), catalog) { }

    public WorldBrowserView(WorldBrowserViewModel model, WorldCatalog catalog)
    {
        _catalog = catalog; _model = model; DataContext = model;
        // The site's page background is the darkest surface and its rows and cards sit on it one step up; here
        // that is the theme's shell under panel-coloured cards, light or dark.
        Bind(BackgroundProperty, new DynamicResourceExtension("ShellBrush"));
        DirectoryLook.AddLinkStyles(Styles);
        var heading = Ui.TextKey(nameof(L.FindAMUD), 22);
        heading.Name = "DirectoryHeading";
        heading.FontWeight = FontWeight.SemiBold;
        heading.VerticalAlignment = VerticalAlignment.Center;
        _refresh = Ui.ToolbarIconKey(new Button { Name = "RefreshDirectory", Command = _model.RefreshCommand },
            "M 13,5 A 5.5,5.5 0 1 0 13.2,10 M 13,1 V 5 H 9", nameof(L.RefreshDirectory), inset: true);
        _refresh.VerticalAlignment = VerticalAlignment.Center;
        _back = Ui.ButtonKey(nameof(L.BackToWorlds), BackToResults, "quiet");
        _back.Name = "DirectoryBack"; _back.IsVisible = false; _back.FontSize = 12;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Children = { _back, heading, _refresh } };
        Grid.SetColumn(heading, 1); Grid.SetColumn(_refresh, 2);
        _search.TextChanged += (_, _) => Filter();
        _connectionFilter.SelectionChanged += (_, _) => Filter();
        _onlineChoice.SelectionChanged += (_, _) => Filter();
        _onlineChoice.Bind(ToolTip.TipProperty, LocalizedText.Binding(nameof(L.BasedOnTheDirectorySLatestReportNotA)));
        StyleResults();
        _list.ItemTemplate = new FuncDataTemplate<WorldListing>((world, _) => world is null ? null : new DirectoryWorldCard(world,
            Explore, SaveWorld, _model.IsWorldSaved, (w, token) => _rowThumbnails?.GetAsync(w, token) ?? Task.FromResult<Bitmap?>(null), _model.Now));
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _list.AddHandler(KeyDownEvent, (_, e) =>
        {
            var onButton = e.Source is Button || e.Source is Visual visual && visual.GetVisualAncestors().OfType<Button>().Any();
            if (e.Key == Key.Enter && !onButton && _list.SelectedItem is WorldListing world)
            { Explore(world); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        _list.SelectionChanged += (_, _) =>
        {
            if (_renderingResults) return;
            _model.SelectedWorld = _list.SelectedItem as WorldListing;
            ShowSelection();
        };
        _sort.Bind(ToolTip.TipProperty, LocalizedText.Binding(nameof(L.SortWorldsByRelevanceNamePopulationRatingOrDate)));
        _sort.Bind(Avalonia.Automation.AutomationProperties.NameProperty, LocalizedText.Binding(nameof(L.SortWorldsBy)));
        _sort.SelectionChanged += (_, _) => Filter();

        _artPlaceholder.HorizontalAlignment = HorizontalAlignment.Center; _artPlaceholder.VerticalAlignment = VerticalAlignment.Center;
        _artFrame = new Border
        {
            Name = "DirectoryArtworkFrame", ClipToBounds = true, CornerRadius = new CornerRadius(12), Height = 320,
            Child = new Panel { Children = { _artPlaceholder, _image, _heroScrim } }
        };
        _artFrame.Paint(Border.BackgroundProperty, "PanelBrush");
        _listingToolbar = new Border { Name = "DirectoryListingToolbar", Child = _listingActions, IsVisible = false };
        _detailScroll = new ScrollViewer { Name = "DirectoryDetailsScroll", Content = _details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _detailScroll.ScrollChanged += (_, _) => { if (!_closed && !_restoringScroll) _model.DetailScrollOffset = _detailScroll.Offset.Y; };
        _detailPage = _detailScroll;

        var introHeading = DirectoryLook.Label(L.FindYourNextWorld, 30, weight: FontWeight.Bold);
        introHeading.Bind(TextBlock.TextProperty, LocalizedText.Binding(nameof(L.FindYourNextWorld)));
        var lede = DirectoryLook.Label(L.DirectoryLede, 16, "MutedBrush");
        lede.Bind(TextBlock.TextProperty, LocalizedText.Binding(nameof(L.DirectoryLede)));
        _intro = new StackPanel { Name = "DirectoryIntro", Spacing = 4, Margin = new Thickness(0, 4, 0, 6), Children = { introHeading, lede } };
        var footer = new StackPanel { Spacing = 4, Children = { _feedback, _status } };
        var bar = CreateFilterBar();
        var counts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _count, _resetFilters } };
        _count.Name = "DirectoryCount"; _count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_resetFilters, 1);
        var results = new Panel { Children = { _list, _emptyResults } };
        _resultsPage = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 12,
            Children = { _intro, bar, counts, results } };
        Grid.SetRow(bar, 1); Grid.SetRow(counts, 2); Grid.SetRow(results, 3);
        var body = new Panel { Children = { _resultsPage, _detailPage } };
        var contents = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 6, Margin = new Thickness(18, 14, 18, 10), Children = { body, footer } };
        Grid.SetRow(footer, 1);
        Grid.SetRow(contents, 1);
        Content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { Ui.Toolbar(header, "DirectoryTitleBar"), contents } };
        _timer.Tick += async (_, _) => { if (_model.IsExploring && _bitmap is null && !_artLoading && _list.SelectedItem is WorldListing world) await LoadArtAsync(world); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _model.IsExploring) { BackToResults(); e.Handled = true; } };
        SizeChanged += (_, _) => Reflow();
        _ready = true; ApplyQueryToControls();
        UpdatePage();
    }

    private void Reflow()
    {
        // The intro is the site's page heading; a short dock gives its room to the results instead.
        _intro.IsVisible = Bounds.Height >= 760;
        var narrow = Bounds.Width < NarrowBelow;
        if (_narrow == narrow) return;
        _narrow = narrow;
        foreach (var layout in _narrowLayouts) layout(narrow);
        _artFrame.Height = narrow ? 240 : 320;
    }

    private void Explore(WorldListing world)
    {
        _model.ResultsScrollOffset = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset.Y ?? 0;
        _list.SelectedItem = world;
        _model.SelectedWorld = world;
        _model.IsExploring = true;
        _back.Focus();
    }

    private void SaveWorld(WorldListing world)
    {
        _list.SelectedItem = world; _model.SelectedWorld = world;
        _model.SaveSelectedWorld();
        RefreshSavedRows();
    }

    private void BackToResults()
    {
        _model.IsExploring = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed || _model.IsExploring) return;
            if (_list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                scroll.Offset = new Vector(0, _model.ResultsScrollOffset);
            if (_list.ContainerFromIndex(_list.SelectedIndex) is { } selected) selected.Focus();
            else _search.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void UpdatePage()
    {
        _resultsPage.IsVisible = !_model.IsExploring;
        _detailPage.IsVisible = _back.IsVisible = _model.IsExploring;
    }

    private void RefreshSavedRows()
    {
        foreach (var card in _list.GetVisualDescendants().OfType<DirectoryWorldCard>()) card.RefreshSaved();
    }

    private void StyleResults()
    {
        // The rows are the site's cards; the list keeps the app's keyboard and focus behaviour but paints nothing
        // of its own. Selection is an accent outline, hover a stronger rule, as the site's row hover is.
        _list.Classes.Add("world-list");
        _list.Styles.Add(new Style(s => s.OfType<ListBox>().Class("world-list").Descendant().OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(0)), new Setter(MarginProperty, new Thickness(0, 0, 0, 14)),
                new Setter(BackgroundProperty, Brushes.Transparent) }
        });
        foreach (var state in new[] { ":pointerover", ":selected", ":focus-visible" })
            _list.Styles.Add(new Style(s => s.OfType<ListBox>().Class("world-list").Descendant().OfType<ListBoxItem>().Class(state))
            { Setters = { new Setter(BackgroundProperty, Brushes.Transparent) } });
        _list.Styles.Add(new Style(s => s.OfType<Border>().Class("directory-result"))
        {
            Setters = { new Setter(Border.BackgroundProperty, new DynamicResourceExtension("PanelBrush")),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("LineBrush")) }
        });
        _list.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":pointerover").Descendant().OfType<Border>().Class("directory-result"))
        {
            Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("MutedBrush")) }
        });
        _list.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<Border>().Class("directory-result"))
        {
            Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")) }
        });
        _list.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":focus-visible").Descendant().OfType<Border>().Class("directory-result"))
        {
            Setters = { new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("TextBrush")) }
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _closed = false; _restoringScroll = true;
        // Two pixels per unit of the site's 400 by 160 plate, cropped to cover it.
        _rowThumbnails = new(_catalog, 800, 320, cacheBitmaps: false, kind: WorldArtwork.Generated, cover: true);
        if (_lifetime.IsCancellationRequested) { _lifetime.Dispose(); _lifetime = new(); }
        _model.PropertyChanged += ModelChanged;
        _model.Attach(action => Dispatcher.UIThread.Post(action));
        // Initial values may be unchanged (especially the shared empty results array).
        // Render the complete snapshot instead of depending on change notifications.
        ModelChanged(_model, new PropertyChangedEventArgs(null));
        var resultsOffset = _model.ResultsScrollOffset;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                scroll.Offset = new Vector(0, resultsOffset);
        }, DispatcherPriority.Loaded);
        _timer.Start();
        _ = _model.LoadAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (!_restoringScroll) _model.DetailScrollOffset = _detailScroll.Offset.Y;
        if (!_model.IsExploring) _model.ResultsScrollOffset = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset.Y ?? 0;
        _closed = true; _timer.Stop();
        _model.PropertyChanged -= ModelChanged;
        _model.Detach();
        _lifetime.Cancel(); _selection?.Cancel(); _selection?.Dispose(); _selection = null;
        ReleaseArtwork();
        _rowThumbnails?.Dispose(); _rowThumbnails = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The name and tagline sit on a photograph, not on the theme: over a picture they are fixed light ink on
    /// the fixed dark scrim; over the initials plate they follow the theme like the rest of the page.</summary>
    private void InkHero(bool onPicture)
    {
        _heroScrim.IsVisible = onPicture;
        if (_heroTitle is { } title)
        {
            if (onPicture) title.Foreground = HeroTitleInk; else title.Paint(TextBlock.ForegroundProperty, "TextBrush");
        }
        if (_heroTagline is { } tagline)
        {
            if (onPicture) tagline.Foreground = HeroTaglineInk; else tagline.Paint(TextBlock.ForegroundProperty, "MutedBrush");
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (e.PropertyName is null or nameof(WorldBrowserViewModel.Query)) ApplyQueryToControls();
        if (e.PropertyName is null or nameof(WorldBrowserViewModel.FacetOptions)) RefreshFilterOptions();
        if (e.PropertyName is nameof(WorldBrowserViewModel.IsExploring)) { UpdatePage(); ShowSelection(); }
        if (e.PropertyName is null or nameof(WorldBrowserViewModel.Results))
        {
            _renderingResults = true;
            _list.ItemsSource = _model.Results;
            _list.SelectedItem = _model.SelectedWorld;
            _renderingResults = false;
            ShowSelection();
            _emptyResults.Children.Clear();
            _emptyResults.Children.Add(Ui.Text(_model.EmptyTitle, 18));
            _emptyResults.Children.Add(Ui.Text(_model.EmptyDescription, 13, "muted"));
            _emptyResults.IsVisible = _model.Results.Count == 0;
        }
        if (e.PropertyName is null or nameof(WorldBrowserViewModel.SavedWorlds)) RefreshSavedRows();
        UpdatePage();
        _refresh.IsEnabled = _model.CanRefresh;
        _count.Text = _model.Count;
        _status.Text = _model.Status;
        _feedback.Text = _model.Feedback;
        _feedback.IsVisible = !string.IsNullOrWhiteSpace(_model.Feedback);
        _status.IsVisible = !string.IsNullOrWhiteSpace(_model.Status);
        _filterSummary.Text = _model.FilterSummary;
        _filterHint.Text = _model.FilterHint;
        _resetFilters.IsVisible = _model.HasFilters;
    }

    private void Filter()
    {
        if (!_ready || _updatingFilters) return;
        _model.Query = new WorldBrowserQuery
        {
            Search = _search.Text ?? "", Connection = _connectionFilter.SelectedIndex,
            OnlineOnly = _onlineChoice.SelectedIndex == 1, Sort = _sort.SelectedIndex,
            Facets = _facets.Where(f => f.Input.SelectedIndex > 0).ToDictionary(f => f.Key, f => f.Input.SelectedItem as string ?? ""),
            MinimumPlayers = _minimumPlayers.Value, MaximumPlayers = _maximumPlayers.Value,
            Rating = _rating.SelectedIndex, TlsOnly = _tlsFilter.IsChecked == true, ShowAdult = _adultFilter.IsChecked == true
        };
    }

    private void ReleaseArtwork()
    {
        _image.Source = null; _image.IsVisible = false; _bitmap?.Dispose(); _bitmap = null;
        _banner.Source = null; _bannerBitmap?.Dispose(); _bannerBitmap = null;
    }

    private void ShowSelection()
    {
        _selection?.Cancel(); _selection?.Dispose();
        _selection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        ReleaseArtwork();
        _artLoading = false;
        var selectedId = (_list.SelectedItem as WorldListing)?.Id;
        var sameWorld = selectedId == _model.ScrollWorldId;
        var offset = sameWorld ? _model.DetailScrollOffset : 0;
        var version = ++_detailRenderVersion;
        _restoringScroll = true;
        _model.ScrollWorldId = selectedId;
        _model.DetailScrollOffset = offset;
        _detailScroll.Offset = new Vector(0, offset);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && version == _detailRenderVersion)
            {
                _detailScroll.Offset = new Vector(0, offset);
                _restoringScroll = false;
            }
        }, DispatcherPriority.Loaded);
        // The hero, the actions and the banner outlive one world's page; free them from the last one first.
        foreach (var kept in new Control[] { _artFrame, _listingToolbar, _banner }) Detach(kept);
        _details.Children.Clear();
        _narrowLayouts.Clear(); _narrow = null;
        _listingActions.Children.Clear();
        _listingToolbar.IsVisible = _list.SelectedItem is WorldListing;
        if (_list.SelectedItem is not WorldListing world)
        {
            _details.Children.Add(Ui.Text(_model.EmptyTitle, 17));
            _details.Children.Add(Ui.Text(_model.EmptyDescription, 13, "muted"));
            return;
        }
        BuildWorldPage(world);
        Reflow();
        if (_model.IsExploring) _ = LoadArtAsync(world);
    }

    private void AddLink(Panel links, string label, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) return;
        var button = DirectoryLook.Link(label, async () =>
        {
            try { if (TopLevel.GetTopLevel(this) is { } topLevel) await topLevel.Launcher.LaunchUriAsync(uri); }
            catch (Exception) { _model.ReportLinkFailure(); }
        }, 14, weight: FontWeight.Normal);
        Avalonia.Automation.AutomationProperties.SetName(button, label.TrimEnd(' ', '↗'));
        links.Children.Add(button);
    }

    private async Task LoadArtAsync(WorldListing world)
    {
        var selection = _selection;
        if (selection is null || selection.IsCancellationRequested) return;
        var token = selection.Token;
        _artLoading = true;
        try
        {
            // The hero is the illustration the site shows; a supplied banner goes in "A glimpse inside" below it.
            // A world with only a banner shows the banner as its hero.
            var generated = world.HasGeneratedArtwork ? await _catalog.GetArtAsync(world, WorldArtwork.Generated, token) : null;
            var bytes = generated ?? await _catalog.GetArtAsync(world, WorldArtwork.Supplied, token);
            if (token.IsCancellationRequested || _closed || _selection != selection) return;
            if (bytes is null) { _artStatus.Text = L.YouCanStillBrowseTheDetailsAndConnect; return; }
            var bitmap = Decode(bytes);
            _bitmap?.Dispose(); _bitmap = bitmap; _image.Source = bitmap; _image.IsVisible = true;
            _artPlaceholder.IsVisible = false; InkHero(true);
            _artStatus.Text = generated is not null ? L.AIIllustrationInspiredByThisWorldSDescription : L.Format(L.SuppliedArtwork, world.Source.Name);
            if (generated is not null && world.HasSuppliedArtwork && await _catalog.GetArtAsync(world, WorldArtwork.Supplied, token) is { } supplied
                && !token.IsCancellationRequested && !_closed && _selection == selection)
            {
                var banner = Decode(supplied);
                _bannerBitmap?.Dispose(); _bannerBitmap = banner; _banner.Source = banner;
                ShowGlimpse(world);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && !_closed && _selection == selection)
                _artStatus.Text = L.WorldDetailsAreStillAvailableBelow;
        }
        finally { if (_selection == selection) _artLoading = false; }
    }

    private static void Detach(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel: panel.Children.Remove(control); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
    }

    private static Bitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }
}
