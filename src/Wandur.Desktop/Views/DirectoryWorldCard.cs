using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Wandur.Core.Discovery;
using Wandur.Desktop.Services;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Views;

/// <summary>A virtualized directory row. Reflows from the dock's width, never the screen resolution.</summary>
internal sealed class DirectoryWorldCard : Border
{
    protected override Type StyleKeyOverride => typeof(Border);
    private readonly WorldListing _world;
    private readonly Func<WorldListing, CancellationToken, Task<Bitmap?>> _loadArtwork;
    private CancellationTokenSource? _load;
    private Bitmap? _bitmap;
    private readonly Border _thumbnail;
    private readonly Image _image = new() { Name = "DirectoryRowArtwork", Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly TextBlock _initials;
    private readonly TextBlock _description;
    private readonly TextBlock _population;
    private readonly WrapPanel _actions;
    private readonly Grid _footer;
    private readonly Button _save;
    private readonly Func<WorldListing, bool> _isSaved;
    private int _attachment;
    private int _layoutMode = -1;

    public DirectoryWorldCard(WorldListing world, Action<WorldListing> explore, Action<WorldListing> save,
        Func<WorldListing, bool> isSaved, Func<WorldListing, CancellationToken, Task<Bitmap?>> loadArtwork)
    {
        _world = world; _isSaved = isSaved; _loadArtwork = loadArtwork;
        Name = "DirectoryResultCard"; Classes.Add("directory-result");
        Padding = new Thickness(12); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6);
        _initials = Ui.Text(WorldThumbnails.Initials(world.Name), 24);
        _initials.HorizontalAlignment = HorizontalAlignment.Center; _initials.VerticalAlignment = VerticalAlignment.Center;
        _initials.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush"));
        _thumbnail = new Border { Name = "DirectoryResultIdentity", Width = 136, Height = 92, CornerRadius = new CornerRadius(4),
            ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, Child = new Panel { Children = { _initials, _image } } };
        _thumbnail.Bind(BackgroundProperty, new DynamicResourceExtension("ShellBrush"));
        ToolTip.SetTip(_thumbnail, world.HasSuppliedArtwork ? L.Format(L.SuppliedArtwork, world.Source.Name) : L.AIIllustrationInspiredByThisWorldSDescription);
        var title = Ui.Text(world.Name, 18);
        title.FontWeight = FontWeight.SemiBold; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var tags = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var tag in new[] { world.Features.Theme }.Concat(world.Tags).Append(world.Features.Kind)
                     .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2))
        {
            var label = Ui.Text(tag, 11); label.MaxLines = 1; label.TextTrimming = TextTrimming.CharacterEllipsis;
            var badge = new Border { Padding = new Thickness(7, 3), Margin = new Thickness(0, 0, 6, 4),
                MaxWidth = 155, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = label };
            badge.Bind(BorderBrushProperty, new DynamicResourceExtension("LineBrush"));
            tags.Children.Add(badge);
        }
        var identity = new StackPanel { Spacing = 6, Children = { title, tags } };
        _description = Ui.Text(string.IsNullOrWhiteSpace(world.Summary) ? world.Description : world.Summary, 13);
        _description.MaxLines = 2; _description.TextTrimming = TextTrimming.CharacterEllipsis;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 14, RowSpacing = 6, Children = { _thumbnail, identity, _description } };
        Grid.SetColumn(identity, 1); Grid.SetRow(_description, 1);
        var population = world.Population.LatestCount is { } n ? L.Format(L.PlayersLastObserved, n) : world.PopulationSummary;
        _population = Ui.Text(population, 11, "muted");
        _population.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_population, world.StatusText);
        _save = Ui.ButtonKey(nameof(L.AddToMyWorlds), () => { save(world); RefreshSaved(); });
        _save.Name = "DirectoryRowSave"; _save.FontSize = 12;
        var open = Ui.ButtonKey(nameof(L.ExploreWorld), () => explore(world));
        open.Name = "DirectoryRowExplore"; open.FontSize = 12;
        open.Bind(Button.BorderBrushProperty, new DynamicResourceExtension("AccentBrush"));
        foreach (var button in new[] { _save, open }) button.Margin = new Thickness(6, 3, 0, 0);
        _actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _save, open } };
        _footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 4, Children = { _population, _actions } };
        var footerEdge = new Border { Padding = new Thickness(0, 6, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), Child = _footer };
        footerEdge.Bind(BorderBrushProperty, new DynamicResourceExtension("LineBrush"));
        Child = new StackPanel { Spacing = 10, Children = { body, footerEdge } };
        RefreshSaved();
    }

    public void RefreshSaved()
    {
        var saved = _isSaved(_world);
        _save.Content = saved ? L.WorldSaved : "+ " + L.AddToMyWorlds;
        _save.IsEnabled = _world.CanConnect && !saved;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var mode = availableSize.Width < 480 ? 0 : availableSize.Width < 700 ? 1 : 2;
        if (_layoutMode != mode)
        {
            _layoutMode = mode;
            _thumbnail.Width = mode == 0 ? 72 : mode == 1 ? 100 : 136;
            _thumbnail.Height = mode == 0 ? 64 : mode == 1 ? 78 : 92;
            Grid.SetRowSpan(_thumbnail, mode == 0 ? 1 : 2);
            Grid.SetColumn(_description, mode == 0 ? 0 : 1);
            Grid.SetColumnSpan(_description, mode == 0 ? 2 : 1);
        }
        // Button widths vary by language. Wrap based on their measured width, not English assumptions.
        _actions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _population.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var stacked = _actions.DesiredSize.Width + _population.DesiredSize.Width + 44 > availableSize.Width;
        _footer.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(_actions, stacked ? 0 : 1); Grid.SetRow(_actions, stacked ? 1 : 0);
        return base.MeasureOverride(availableSize);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var attachment = ++_attachment;
        _load = new CancellationTokenSource();
        var token = _load.Token;
        RefreshSaved();
        Dispatcher.UIThread.Post(async () =>
        {
            if (attachment != _attachment) return;
            var bitmap = await _loadArtwork(_world, token);
            if (attachment != _attachment || token.IsCancellationRequested) { bitmap?.Dispose(); return; }
            if (bitmap is null) return;
            _bitmap = bitmap; _image.Source = bitmap; _initials.IsVisible = false;
        });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ++_attachment; _image.Source = null; _initials.IsVisible = true;
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _bitmap?.Dispose(); _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }
}
