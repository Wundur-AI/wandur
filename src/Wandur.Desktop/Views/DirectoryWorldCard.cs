using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Wandur.Core.Discovery;
using Wandur.Desktop.Services;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Views;

/// <summary>
/// One world in the directory, laid out as the site's row: a wide art plate, the name, a pill line (genre, two
/// tags, "N online"), a two-line blurb, one bottom pill, then a rule and the accent "Explore world". It reflows
/// from the dock's width, never the screen's, and stacks into a card on a narrow dock as the site does on a phone.
/// </summary>
internal sealed class DirectoryWorldCard : Border
{
    /// <summary>The site's plate: 400 by 160, five to two, cropped to fill.</summary>
    public const double PlateWidth = 400, PlateAspect = 2.5;
    /// <summary>Below this row width the plate goes on top, as the site does on a phone.</summary>
    public const double StackedBelow = 480;
    /// <summary>Below this row width the way in leaves its ruled column and sits under the text.</summary>
    public const double SplitBelow = 760;
    /// <summary>The site's row is never shorter than its 160 plate; a compact row keeps a little less.</summary>
    public const double WideMinHeight = 160, CompactMinHeight = 128;
    protected override Type StyleKeyOverride => typeof(Border);
    private readonly WorldListing _world;
    private readonly Func<WorldListing, CancellationToken, Task<Bitmap?>> _loadArtwork;
    private readonly Func<WorldListing, bool> _isSaved;
    private CancellationTokenSource? _load;
    private Bitmap? _bitmap;
    private readonly Grid _layout = new();
    private readonly Border _plate;
    private readonly Image _image = new() { Name = "DirectoryRowArtwork", Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly TextBlock _initials;
    private readonly StackPanel _copy;
    private readonly Border _go;
    private readonly FlowPanel _goContent;
    private readonly Button _save;
    private int _attachment;
    private Mode? _mode;
    private double _plateWidth = -1;

    public DirectoryWorldCard(WorldListing world, Action<WorldListing> explore, Action<WorldListing> save,
        Func<WorldListing, bool> isSaved, Func<WorldListing, CancellationToken, Task<Bitmap?>> loadArtwork, DateTimeOffset? now = null)
    {
        _world = world; _isSaved = isSaved; _loadArtwork = loadArtwork;
        Name = "DirectoryResultCard"; Classes.Add("directory-result");
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(12); ClipToBounds = true;

        _initials = DirectoryLook.Label(WorldThumbnails.Initials(world.Name), 28);
        _initials.HorizontalAlignment = HorizontalAlignment.Center; _initials.VerticalAlignment = VerticalAlignment.Center;
        _plate = new Border { Name = "DirectoryResultIdentity", ClipToBounds = true, Child = new CoverPanel { Children = { _initials, _image } } };
        _plate.Paint(BackgroundProperty, DirectoryLook.PlateBrush);
        ToolTip.SetTip(_plate, world.HasGeneratedArtwork || !world.HasSuppliedArtwork
            ? L.AIIllustrationInspiredByThisWorldSDescription : L.Format(L.SuppliedArtwork, world.Source.Name));

        var title = DirectoryLook.Label(world.Name, 20, weight: FontWeight.Bold);
        title.Name = "DirectoryRowTitle"; title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var (top, bottom) = DirectoryLook.RowPills(world);
        var pills = new FlowPanel { Name = "DirectoryRowPills", Gap = 6, LineGap = 6 };
        foreach (var pill in top) pills.Children.Add(DirectoryLook.Pill(pill));
        if (world.IsAdult) { var adult = DirectoryLook.Pill(L.AdultChip); adult.Name = "DirectoryRowAdult"; pills.Children.Add(adult); }
        // Online status sits at the end of the title line on every row, rather than wherever the tags happened to wrap.
        var heading = new DockPanel { Name = "DirectoryRowHeading", LastChildFill = true };
        if (DirectoryLook.OnlineText(world, now ?? DateTimeOffset.UtcNow) is { } online)
        {
            var live = DirectoryLook.Live(online);
            live.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(live, Avalonia.Controls.Dock.Right);
            heading.Children.Add(live);
        }
        heading.Children.Add(title);
        var blurbText = string.IsNullOrWhiteSpace(world.Summary) ? world.Description : world.Summary;
        var blurb = DirectoryLook.Label(blurbText.ReplaceLineEndings(" ").Trim(), 14, "MutedBrush");
        blurb.Name = "DirectoryRowBlurb"; blurb.MaxLines = 2; blurb.TextTrimming = TextTrimming.WordEllipsis; blurb.LineHeight = 20;
        _copy = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { heading } };
        if (pills.Children.Count > 0) _copy.Children.Add(pills);
        if (blurbText.Length > 0) _copy.Children.Add(blurb);
        if (world.BeginnerFriendly == true || bottom is not null)
        {
            var tag = world.BeginnerFriendly == true ? DirectoryLook.Beginner() : DirectoryLook.Pill(bottom!);
            tag.HorizontalAlignment = HorizontalAlignment.Left; tag.Name ??= "DirectoryRowTag";
            _copy.Children.Add(tag);
        }

        var open = DirectoryLook.Link(L.ExploreWorld, () => explore(world), 16);
        open.Name = "DirectoryRowExplore";
        // Saving is the client's own addition to the site's row: quiet, under the way in.
        _save = DirectoryLook.Link("", () => { save(world); RefreshSaved(); }, 12, "MutedBrush", FontWeight.Normal);
        _save.Name = "DirectoryRowSave";
        _goContent = new FlowPanel { Gap = 6, LineGap = 6, VerticalAlignment = VerticalAlignment.Center, Children = { open, _save } };
        _go = new Border { Child = _goContent };
        _go.Paint(BorderBrushProperty, "LineBrush");
        _layout.Children.Add(_plate); _layout.Children.Add(_copy); _layout.Children.Add(_go);
        Child = _layout;
        RefreshSaved();
    }

    public void RefreshSaved()
    {
        var saved = _isSaved(_world);
        _save.Content = saved ? L.WorldSaved : "+ " + L.AddToMyWorlds;
        _save.IsEnabled = _world.CanConnect && !saved;
        _save.IsVisible = _world.CanConnect;
        // The way-in column is sized to its text, which just changed.
        _mode = null; InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : PlateWidth * 2;
        var mode = width < StackedBelow ? Mode.Stacked : width < SplitBelow ? Mode.Compact : Mode.Wide;
        // The plate keeps the site's 400 wide where the row has room, and gives way on a narrower dock so the
        // name and blurb keep a readable measure.
        var plate = mode switch
        {
            Mode.Stacked => width,
            Mode.Compact => Math.Clamp(Math.Round(width * .36), 180, 280),
            _ => Math.Clamp(Math.Round(width * .34), 240, PlateWidth)
        };
        if (_mode != mode || Math.Abs(_plateWidth - plate) > .5)
        {
            _mode = mode; _plateWidth = plate;
            ApplyLayout(mode, plate);
            _goContent.InvalidateMeasure();
        }
        return base.MeasureOverride(availableSize);
    }

    private enum Mode { Stacked, Compact, Wide }

    private void ApplyLayout(Mode mode, double plate)
    {
        _layout.ColumnDefinitions.Clear(); _layout.RowDefinitions.Clear();
        Grid.SetRowSpan(_plate, 1);
        if (mode == Mode.Stacked)
        {
            // A phone-width dock: the plate on top, the way in along the bottom, as the site under 800 pixels.
            _layout.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            _plate.Width = double.NaN; _plate.MinHeight = 0;
            _plate.Height = Math.Clamp(Math.Round(plate / PlateAspect), 110, 160);
            Grid.SetRow(_plate, 0); Grid.SetColumn(_plate, 0);
            Grid.SetRow(_copy, 1); Grid.SetColumn(_copy, 0); _copy.Margin = new Thickness(16, 14, 16, 14);
            Grid.SetRow(_go, 2); Grid.SetColumn(_go, 0);
            _go.BorderThickness = new Thickness(0, 1, 0, 0); _go.Margin = default; _go.Padding = new Thickness(16, 10);
            _goContent.Orientation = Orientation.Horizontal; _goContent.Gap = 18;
        }
        else if (mode == Mode.Compact)
        {
            // A dock too narrow for the ruled column: the way in moves under the text, beside the plate.
            _layout.ColumnDefinitions = new ColumnDefinitions("Auto,*");
            _layout.RowDefinitions = new RowDefinitions("*,Auto");
            _plate.Width = plate; _plate.Height = double.NaN; _plate.MinHeight = Math.Max(CompactMinHeight, Math.Round(plate / PlateAspect));
            Grid.SetRow(_plate, 0); Grid.SetColumn(_plate, 0); Grid.SetRowSpan(_plate, 2);
            Grid.SetRow(_copy, 0); Grid.SetColumn(_copy, 1); _copy.Margin = new Thickness(18, 12, 14, 6);
            Grid.SetRow(_go, 1); Grid.SetColumn(_go, 1);
            _go.BorderThickness = default; _go.Margin = default; _go.Padding = new Thickness(18, 0, 14, 12);
            _goContent.Orientation = Orientation.Horizontal; _goContent.Gap = 16;
        }
        else
        {
            _plate.Width = plate; _plate.Height = double.NaN; _plate.MinHeight = Math.Max(WideMinHeight, Math.Round(plate / PlateAspect));
            Grid.SetRow(_plate, 0); Grid.SetColumn(_plate, 0);
            Grid.SetRow(_copy, 0); Grid.SetColumn(_copy, 1); _copy.Margin = new Thickness(22, 12, 18, 12);
            Grid.SetRow(_go, 0); Grid.SetColumn(_go, 2);
            _go.BorderThickness = new Thickness(1, 0, 0, 0); _go.Margin = new Thickness(0, 22); _go.Padding = new Thickness(26, 0);
            _goContent.Orientation = Orientation.Vertical; _goContent.Gap = 6;
            // Every column but the text has a fixed width, so the text is measured once at the width it is given
            // and its pill line cannot wrap in measure and then sit on one line, leaving a gap under it.
            _go.Measure(Size.Infinity);
            _layout.ColumnDefinitions = new ColumnDefinitions(FormattableString.Invariant($"{plate},*,{Math.Ceiling(_go.DesiredSize.Width)}"));
        }
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

/// <summary>
/// Fills whatever box its parent gives it and asks for nothing itself, so a picture cropped to cover a plate never
/// makes the row taller than its text (an image stretched to fill would otherwise ask for its own aspect's height).
/// </summary>
internal sealed class CoverPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
