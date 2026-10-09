using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace Wandur.Desktop;

/// <summary>
/// One icon button a panel puts in its title bar: a glyph, a localized tooltip (also the accessible name), and either
/// a command or, for a toggle, the property its checked state follows. <see cref="Name"/> names the button, so tests
/// and automation find it wherever it is shown.
/// </summary>
public sealed class PanelHeaderAction(string name, string geometry, string tipKey)
{
    public string Name { get; } = name;
    public string Geometry { get; } = geometry;
    /// <summary>The localization key of the tooltip and accessible name.</summary>
    public string TipKey { get; } = tipKey;
    public ICommand? Command { get; init; }
    public object? CommandParameter { get; init; }
    /// <summary>What <see cref="CheckedPath"/> and <see cref="VisiblePath"/> are read from (the panel's view model).</summary>
    public object? Source { get; init; }
    /// <summary>A toggle's state, bound both ways; null for a plain button.</summary>
    public string? CheckedPath { get; init; }
    /// <summary>A bool that shows or hides the button; null when it is always shown.</summary>
    public string? VisiblePath { get; init; }

    /// <summary>A button for this action; <paramref name="inHeader"/> gives it the header's size and glyph colour.</summary>
    internal Button Create(bool inHeader = true)
    {
        Button button = CheckedPath is null ? new Button() : new ToggleButton();
        button.Name = Name;
        Ui.ToolbarIconKey(button, Geometry, TipKey);
        if (inHeader) button.Classes.Add("panel-action");
        button.Command = Command;
        button.CommandParameter = CommandParameter;
        if (CheckedPath is not null) button.Bind(ToggleButton.IsCheckedProperty, new Binding(CheckedPath) { Source = Source, Mode = BindingMode.TwoWay });
        if (VisiblePath is not null) button.Bind(Visual.IsVisibleProperty, new Binding(VisiblePath) { Source = Source });
        return button;
    }
}

/// <summary>
/// Panel actions in the title bar. A panel's view sets <see cref="ActionsProperty"/> on itself; the dock header of
/// the panel it is shown in then draws those actions between the title and the dock's own buttons, right-aligned.
/// When the header is too narrow to keep <see cref="MinimumTitleCharacters"/> of the title readable beside them, the
/// actions move to one row directly under the header, and back once it is wide enough again. There is no overflow
/// menu. Dock's ToolChromeControl template is left as Dock ships it: the actions are added to the free Auto column
/// of its header grid and to an extra row of its root grid when its template is applied.
/// </summary>
public static class PanelHeader
{
    /// <summary>How much of a long title must stay readable before the actions give up their place beside it.</summary>
    public const int MinimumTitleCharacters = 8;

    public static readonly AttachedProperty<IReadOnlyList<PanelHeaderAction>?> ActionsProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, IReadOnlyList<PanelHeaderAction>?>("Actions");

    public static IReadOnlyList<PanelHeaderAction>? GetActions(Control control) => control.GetValue(ActionsProperty);
    public static void SetActions(Control control, IReadOnlyList<PanelHeaderAction>? actions) => control.SetValue(ActionsProperty, actions);

    private static readonly ConditionalWeakTable<ToolChromeControl, PanelHeaderHost> Hosts = new();
    private static readonly ConditionalWeakTable<Control, ToolChromeControl> Declared = new();
    private static bool _installed;

    /// <summary>Hooks every panel header; called once when the application starts.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ToolChromeControl>((chrome, args) => Hosts.GetValue(chrome, c => new PanelHeaderHost(c)).Attach(args.NameScope));
        ActionsProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.OldValue is null && args.NewValue is not null)
            {
                control.AttachedToVisualTree += DeclarerChanged;
                control.DetachedFromVisualTree += DeclarerDetached;
                control.PropertyChanged += DeclarerPropertyChanged;
            }
            else if (args.NewValue is null)
            {
                control.AttachedToVisualTree -= DeclarerChanged;
                control.DetachedFromVisualTree -= DeclarerDetached;
                control.PropertyChanged -= DeclarerPropertyChanged;
            }
            Notify(control);
        });
    }

    /// <summary>
    /// True where the dock's collapse, pin and close buttons show only while the pointer is over the header or the
    /// keyboard is in it (System and Fleet; the styles are in App.axaml), so a header's actions have their room.
    /// </summary>
    public static bool DockButtonsOnHover(Visual chrome) =>
        chrome.FindAncestorOfType<ThemeDockSkinHost>() is { } host
        && (host.Classes.Contains("system") || host.Classes.Contains("fleet") && !host.Classes.Contains("armored"));

    /// <summary>The header showing <paramref name="chrome"/>'s actions; null until its template has been applied.</summary>
    internal static PanelHeaderHost? HostOf(ToolChromeControl chrome) => Hosts.TryGetValue(chrome, out var host) ? host : null;

    private static void DeclarerChanged(object? sender, VisualTreeAttachmentEventArgs args) { if (sender is Control control) Notify(control); }
    private static void DeclarerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty && sender is Control control) Notify(control);
    }
    private static void DeclarerDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control control && Declared.TryGetValue(control, out var chrome))
        {
            Declared.Remove(control);
            HostOf(chrome)?.Refresh();
        }
    }

    private static void Notify(Control control)
    {
        if (control.FindAncestorOfType<ToolChromeControl>() is { } chrome)
        {
            Declared.AddOrUpdate(control, chrome);
            HostOf(chrome)?.Refresh();
        }
        else if (Declared.TryGetValue(control, out var previous)) HostOf(previous)?.Refresh();
    }
}

/// <summary>
/// The same actions as a plain toolbar row, for a view shown where there is no panel header: built on its own in a
/// window, or inside a document. It stays empty and hidden while the view sits in a dock panel, whose header shows
/// the actions instead, so a button's name is never found twice.
/// </summary>
public sealed class PanelActionBar : StackPanel
{
    private readonly Control _declarer;
    private IReadOnlyList<PanelHeaderAction>? _shown;

    public PanelActionBar(Control declarer)
    {
        _declarer = declarer;
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        IsVisible = false;
        declarer.AttachedToVisualTree += (_, _) => Update();
        declarer.PropertyChanged += (_, args) => { if (args.Property == PanelHeader.ActionsProperty) Update(); };
        Update();
    }

    /// <summary>True while the actions are drawn here rather than in a panel header.</summary>
    public bool HasActions => Children.Count > 0;

    private void Update()
    {
        var actions = _declarer.FindAncestorOfType<ToolChromeControl>() is null ? PanelHeader.GetActions(_declarer) : null;
        if (ReferenceEquals(actions, _shown)) return;
        _shown = actions;
        Children.Clear();
        foreach (var action in actions ?? []) Children.Add(action.Create(inHeader: false));
        IsVisible = Children.Count > 0;
    }
}

/// <summary>The actions of one panel header, in the header or in the row under it.</summary>
internal sealed class PanelHeaderHost
{
    private readonly WeakReference<ToolChromeControl> _chrome;
    private readonly StackPanel _inHeader = new() { Name = "PanelHeaderActions", Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
    private readonly StackPanel _inRow = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _row;
    private readonly TextBlock _measure = new();
    private IReadOnlyList<PanelHeaderAction>? _actions;
    private Grid? _header;
    private TextBlock? _title;
    private Border? _border;
    private Panel? _dockButtons;
    private bool _refreshQueued;

    public PanelHeaderHost(ToolChromeControl chrome)
    {
        _chrome = new(chrome);
        // A skin change can make the dock's buttons hover-only (or not), which changes the room the actions have.
        chrome.AttachedToVisualTree += (_, _) => ThemeService.Applied += SkinChanged;
        chrome.DetachedFromVisualTree += (_, _) => ThemeService.Applied -= SkinChanged;
        if (chrome.IsAttachedToVisualTree()) ThemeService.Applied += SkinChanged;
        _row = new Border { Name = "PanelHeaderActionRow", Child = _inRow, IsVisible = false, Padding = new Thickness(6, 2) };
        _row.Classes.Add("panel-action-row");
    }

    /// <summary>The buttons now shown, wherever they are.</summary>
    public IReadOnlyList<Button> Buttons => (IsInRow ? _inRow : _inHeader).Children.OfType<Button>().ToArray();
    /// <summary>True while the header is too narrow and the actions sit in the row under it.</summary>
    public bool IsInRow { get; private set; }
    public Control HeaderPanel => _inHeader;
    public Control Row => _row;

    public void Attach(INameScope scope)
    {
        if (!_chrome.TryGetTarget(out var chrome)) return;
        Detach(_inHeader); Detach(_row);
        if (_header is not null) _header.SizeChanged -= HeaderResized;
        if (_title is not null) _title.PropertyChanged -= TitleChanged;
        _title = scope.Find<TextBlock>("PART_Title");
        _border = scope.Find<Border>("PART_Border");
        var close = scope.Find<Button>("PART_CloseButton");
        _dockButtons = close?.GetVisualParent() as Panel ?? close?.Parent as Panel;
        _header = _dockButtons?.Parent as Grid;
        var root = _border?.Parent as Grid;
        var content = scope.Find<Control>("PART_ContentPresenter");
        if (_header is null || root is null || content is null || _dockButtons is null || _border is null) return;
        _dockButtons.Classes.Add("dock-buttons");
        // The header grid is Dock's "*,Auto,Auto": the title, a free column, then the dock's buttons. The actions
        // take the free column, so they sit right-aligned against the dock buttons, and come before them in tab order.
        _header.Children.Insert(Math.Max(0, _header.Children.IndexOf(_dockButtons)), _inHeader);
        ArrangeColumns();
        if (root.RowDefinitions.Count == 2)
        {
            root.RowDefinitions.Insert(1, new RowDefinition(GridLength.Auto));
            Grid.SetRow(content, 2);
        }
        Grid.SetRow(_row, 1);
        root.Children.Add(_row);
        _row.Bind(Border.BackgroundProperty, new Binding(nameof(Panel.Background)) { Source = scope.Find<Grid>("PART_Grip") });
        _row.Bind(Border.BorderBrushProperty, new Binding(nameof(Border.BorderBrush)) { Source = _border });
        _row.Bind(Layoutable.MarginProperty, new Binding(nameof(Layoutable.Margin)) { Source = _border, Converter = SideMargins.Instance });
        _row.BorderThickness = new Thickness(0, 0, 0, 1);
        _header.SizeChanged += HeaderResized;
        if (_title is not null) _title.PropertyChanged += TitleChanged;
        Refresh();
    }

    private static void Detach(Control control)
    {
        if (control.Parent is Panel panel) panel.Children.Remove(control);
    }

    /// <summary>Looks for the shown view that declares actions, after the current layout change has settled.</summary>
    public void Refresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() => { _refreshQueued = false; Discover(); });
    }

    private void Discover()
    {
        if (!_chrome.TryGetTarget(out var chrome)) return;
        var declarer = chrome.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => PanelHeader.GetActions(c) is not null && c.IsEffectivelyVisible && c.FindAncestorOfType<ToolChromeControl>() == chrome);
        var actions = declarer is null ? null : PanelHeader.GetActions(declarer);
        if (ReferenceEquals(actions, _actions)) { Place(); return; }
        _actions = actions;
        _inHeader.Children.Clear();
        _inRow.Children.Clear();
        foreach (var action in actions ?? []) _inHeader.Children.Add(action.Create());
        IsInRow = false;
        Place();
    }

    private void HeaderResized(object? sender, SizeChangedEventArgs args) => Place();
    private void TitleChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBlock.TextProperty || args.Property == TextBlock.FontSizeProperty) Place();
    }

    /// <summary>
    /// Keeps the actions beside the title while the title can still show <see cref="PanelHeader.MinimumTitleCharacters"/>
    /// characters (all of it when it is shorter), and puts them in the row under the header otherwise.
    /// </summary>
    private void Place()
    {
        var buttons = (IsInRow ? _inRow : _inHeader).Children.OfType<Button>().ToList();
        if (_header is null || buttons.Count == 0)
        {
            _row.IsVisible = false; _inHeader.IsVisible = buttons.Count > 0;
            return;
        }
        var width = _header.Bounds.Width;
        if (width <= 0) return;
        var fits = TitleRoom() + ActionsWidth(buttons) + DockButtonsWidth() <= width;
        if (fits == !IsInRow) { _row.IsVisible = IsInRow; _inHeader.IsVisible = !IsInRow; return; }
        var from = IsInRow ? _inRow : _inHeader;
        var to = IsInRow ? _inHeader : _inRow;
        foreach (var button in buttons) from.Children.Remove(button);
        foreach (var button in buttons) to.Children.Add(button);
        IsInRow = !fits;
        _row.IsVisible = IsInRow;
        _inHeader.IsVisible = !IsInRow;
    }

    /// <summary>The title's left inset plus the width of its first characters (and an ellipsis), or of all of it.</summary>
    private double TitleRoom()
    {
        if (_title is null) return 0;
        var text = _title.Text ?? "";
        var shown = text.Length <= PanelHeader.MinimumTitleCharacters ? text : text[..PanelHeader.MinimumTitleCharacters] + "…";
        _measure.Text = shown;
        _measure.FontFamily = _title.FontFamily;
        _measure.FontSize = _title.FontSize;
        _measure.FontWeight = _title.FontWeight;
        _measure.FontStyle = _title.FontStyle;
        _measure.Measure(Size.Infinity);
        return _title.Margin.Left + _title.Margin.Right + Math.Ceiling(_measure.DesiredSize.Width);
    }

    // Widths come from the styles (every action and dock button has a fixed width) or the last measure, so nothing is
    // measured again from inside a layout pass.
    private double ActionsWidth(IReadOnlyList<Button> buttons)
    {
        var shown = buttons.Where(b => b.IsVisible).ToList();
        return _inHeader.Margin.Left + _inHeader.Margin.Right + shown.Sum(Width) + Math.Max(0, shown.Count - 1) * _inHeader.Spacing;
    }

    /// <summary>
    /// The dock's own buttons as they take room in the header. Where they show only while the header is hovered or
    /// has keyboard focus (System and Fleet), they take none the rest of the time, and the actions are placed for that:
    /// the title gives way to them while they show, so hovering never moves the actions to the row and back.
    /// </summary>
    private double DockButtonsWidth() =>
        _chrome.TryGetTarget(out var chrome) && PanelHeader.DockButtonsOnHover(chrome) ? 0
            : _dockButtons?.Children.Where(b => b.IsVisible).Sum(Width) ?? 0;

    private void SkinChanged() => Dispatcher.UIThread.Post(() => { ArrangeColumns(); Place(); });

    /// <summary>
    /// Where the dock's buttons are always shown the actions sit between the title and them. Where they show only on
    /// hover (System and Fleet) the actions take the right edge and the dock's buttons open between the title and the
    /// actions, taking the title's room: the actions never move under a pointer that is on its way to one of them.
    /// The tab order stays the panel's actions, then the dock's buttons.
    /// </summary>
    private void ArrangeColumns()
    {
        if (_dockButtons is null || !_chrome.TryGetTarget(out var chrome)) return;
        var onHover = PanelHeader.DockButtonsOnHover(chrome);
        Grid.SetColumn(_inHeader, onHover ? 2 : 1);
        Grid.SetColumn(_dockButtons, onHover ? 1 : 2);
    }

    private static double Width(Control control) =>
        (double.IsNaN(control.Width) ? control.DesiredSize.Width - control.Margin.Left - control.Margin.Right : control.Width) + control.Margin.Left + control.Margin.Right;

    private sealed class SideMargins : Avalonia.Data.Converters.IValueConverter
    {
        public static readonly SideMargins Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is Thickness t ? new Thickness(t.Left, 0, t.Right, 0) : default(Thickness);
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
