using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;
using Wandur.Core.Discovery;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Views;

/// <summary>
/// The pieces the wandur.net directory is built from (pills, chips, the green live dot, the accent link), drawn
/// from the active theme's brushes so the directory reads like the site under every palette, light or dark.
/// </summary>
internal static class DirectoryLook
{
    /// <summary>A leaf, for the beginner friendly pill, as the site draws it.</summary>
    private const string Leaf = "M 13,2 C 6,2 2,6 2,12 L 2,13 L 3,13 C 9,13 13,9 13,3 Z M 3,13 L 9,7";

    public static T Paint<T>(this T control, AvaloniaProperty property, string key) where T : AvaloniaObject
    {
        control.Bind(property, new DynamicResourceExtension(key));
        return control;
    }

    public static TextBlock Label(string text, double size, string brush = "TextBrush", FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        return block.Paint(TextBlock.ForegroundProperty, brush);
    }

    /// <summary>A rounded outline pill with muted text, the site's genre and tag pill.</summary>
    public static Border Pill(string text, double size = 12.5, Thickness? padding = null)
    {
        var label = Label(text, size, "MutedBrush");
        label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
        var pill = new Border
        {
            Classes = { "directory-pill" }, Child = label, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999), Padding = padding ?? new Thickness(10, 2.5), MaxWidth = 220,
            VerticalAlignment = VerticalAlignment.Center
        };
        return pill.Paint(Border.BorderBrushProperty, "LineBrush");
    }

    public static Ellipse Dot(bool live = true, double size = 8) => new Ellipse
    {
        Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center
    }.Paint(Shape.FillProperty, live ? "LiveBrush" : "MutedBrush");

    /// <summary>"N online" or "Online" followed by the green dot, borderless like the site's live pill.</summary>
    public static Control Live(string text, double size = 12.5)
    {
        var label = Label(text, size);
        label.TextWrapping = TextWrapping.NoWrap; label.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Name = "DirectoryLive", Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Children = { label, Dot() }
        };
    }

    /// <summary>The green beginner friendly pill with its leaf.</summary>
    public static Border Beginner(double size = 12.5, Thickness? padding = null)
    {
        var leaf = new Path
        {
            Data = StreamGeometry.Parse(Leaf), Width = size * .85, Height = size * .85, Stretch = Stretch.Uniform,
            StrokeThickness = 1.3, StrokeLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center
        }.Paint(Shape.StrokeProperty, "LiveBrush");
        var label = Label(L.BeginnerFriendly, size, "LiveBrush");
        label.TextWrapping = TextWrapping.NoWrap;
        var pill = new Border
        {
            Name = "DirectoryBeginner", Classes = { "directory-pill" }, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999), Padding = padding ?? new Thickness(9, 2.5), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { leaf, label } }
        };
        ToolTip.SetTip(pill, L.BeginnerFriendlyHint);
        return pill.Paint(Border.BorderBrushProperty, "LiveEdgeBrush");
    }

    /// <summary>A text button with no face: the site's accent "Explore world" link, or a quiet muted one.</summary>
    public static Button Link(string text, Action action, double size, string brush = "AccentTextBrush", FontWeight weight = FontWeight.SemiBold)
    {
        var button = new Button
        {
            Classes = { "directory-link" }, Content = text, FontSize = size, FontWeight = weight, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1.5), BorderBrush = Brushes.Transparent,
            Padding = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Template = new FuncControlTemplate<Button>((owner, _) =>
            {
                var presenter = new ContentPresenter { Name = "PART_ContentPresenter", VerticalContentAlignment = VerticalAlignment.Center };
                presenter.Bind(ContentPresenter.ContentProperty, owner.GetObservable(ContentControl.ContentProperty));
                presenter.Bind(ContentPresenter.PaddingProperty, owner.GetObservable(TemplatedControl.PaddingProperty));
                presenter.Bind(ContentPresenter.BackgroundProperty, owner.GetObservable(TemplatedControl.BackgroundProperty));
                presenter.Bind(ContentPresenter.BorderBrushProperty, owner.GetObservable(TemplatedControl.BorderBrushProperty));
                presenter.Bind(ContentPresenter.BorderThicknessProperty, owner.GetObservable(TemplatedControl.BorderThicknessProperty));
                return presenter;
            })
        };
        button.Paint(TemplatedControl.ForegroundProperty, brush);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Hover and keyboard focus for <see cref="Link"/>: an underline in the link's own colour.</summary>
    public static void AddLinkStyles(Avalonia.Styling.Styles styles)
    {
        foreach (var state in new[] { ":pointerover", ":focus-visible" })
            styles.Add(new Avalonia.Styling.Style(s => s.OfType<Button>().Class("directory-link").Class(state))
            {
                Setters = { new Avalonia.Styling.Setter(TemplatedControl.BorderBrushProperty, new Binding(nameof(TemplatedControl.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.Self) }) }
            });
    }

    /// <summary>The site's pill line and bottom pill for a row: the genre, then up to two tags, then either the
    /// beginner pill (returned as null here) or the first tag not shown yet.</summary>
    public static (IReadOnlyList<string> Top, string? Bottom) RowPills(WorldListing world)
    {
        var genre = world.Features.Theme.Trim();
        var tags = world.Tags.Select(t => t.Trim()).Where(t => t.Length > 0 && !t.Equals(genre, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var top = (genre.Length > 0 ? new[] { genre } : []).Concat(tags.Take(2)).ToArray();
        var bottom = world.BeginnerFriendly == true ? null : tags.Skip(2).FirstOrDefault();
        return (top, bottom);
    }

    /// <summary>"143 online" only for a fresh count Wandur measured; "Online" for any other world reported online.</summary>
    public static string? OnlineText(WorldListing world, DateTimeOffset now) => !world.IsOnline ? null
        : world.LivePlayerCount(now) is { } count ? L.Format(L.OnlineCount, count) : L.StatusOnline;
}

/// <summary>
/// Items in a line with a gap between them, wrapping to further lines as the site's flex-wrap does. It measures
/// and arranges with the same arithmetic, so an item never wraps in measure and then sits on the first line, which
/// would leave an empty line under it. Vertical simply stacks with the gap.
/// </summary>
internal sealed class FlowPanel : Panel
{
    public double Gap { get; set; } = 6;
    public double LineGap { get; set; } = 6;
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    private double _measuredWidth = double.PositiveInfinity;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize.WithHeight(double.PositiveInfinity));
        _measuredWidth = availableSize.Width;
        return Place(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Lines break where they broke in measure: a list that reserves a scroll bar's width while measuring and
        // then arranges without it must not pull an item up and leave its measured line empty.
        Place(Math.Min(finalSize.Width, _measuredWidth), arrange: true);
        return finalSize;
    }

    private Size Place(double width, bool arrange)
    {
        var visible = Children.Where(c => c.IsVisible).ToArray();
        if (Orientation == Orientation.Vertical)
        {
            double y = 0, widest = 0;
            for (var i = 0; i < visible.Length; i++)
            {
                var size = visible[i].DesiredSize;
                if (arrange) visible[i].Arrange(new Rect(0, y, size.Width, size.Height));
                y += size.Height + (i < visible.Length - 1 ? Gap : 0); widest = Math.Max(widest, size.Width);
            }
            return new Size(widest, y);
        }
        // Break into lines first, so each item can be centred on its line as the site's align-items: center does.
        var lines = new List<List<Control>> { new() };
        double x = 0, extent = 0;
        foreach (var child in visible)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width + .5) { lines.Add([]); x = 0; }
            lines[^1].Add(child);
            x += size.Width; extent = Math.Max(extent, x); x += Gap;
        }
        double top = 0;
        foreach (var line in lines.Where(l => l.Count > 0))
        {
            var height = line.Max(c => c.DesiredSize.Height);
            double left = 0;
            foreach (var child in line)
            {
                var size = child.DesiredSize;
                if (arrange) child.Arrange(new Rect(left, top + (height - size.Height) / 2,
                    double.IsFinite(width) ? Math.Min(size.Width, width) : size.Width, size.Height));
                left += size.Width + Gap;
            }
            top += height + LineGap;
        }
        return new Size(extent, visible.Length == 0 ? 0 : top - LineGap);
    }
}
