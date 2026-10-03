using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Wandur.Core.Localization;

namespace Wandur.Desktop;

public sealed partial class MainWindow
{
    private const double TitleActionsWidth = 120;
    private const double TitleLogoSize = 32;
    /// <summary>The plaque icon: Armored keeps its 32 DIP icon, Fleet scales it to its shorter plate.</summary>
    private static double LogoSize => ThemeService.ActiveWindowSkin.IsArmored ? TitleLogoSize : FleetTitleLayout.LogoSize;
    private const double TitleLogoGap = 10;
    private const string FullScreenGlyph = "M 1,6 V 1 H 6 M 10,1 H 15 V 6 M 15,10 V 15 H 10 M 6,15 H 1 V 10";
    private const string ExitFullScreenGlyph = "M 1,6 H 6 V 1 M 10,1 V 6 H 15 M 15,10 H 10 V 15 M 6,15 V 10 H 1";
    private StackPanel _titleActions = null!;
    private Image _titleBarLogo = null!;
    private Grid _plaqueIdentity = null!;
    private Button _exitFullScreenButton = null!;
    private ThemeMenuButton _themeMenuButton = null!;
    private SkinMenuButton _skinMenuButton = null!;
    private bool _fullScreenChrome;
    private bool _fullScreenChromeDirty = true;
    private WindowState? _fullScreenRequest;
    private WindowState _beforeFullScreen = WindowState.Normal;

    private bool WantsFullScreenChrome =>
        _fullScreenRequest == WindowState.FullScreen || WindowState == WindowState.FullScreen;

    private double TitleActionsRightInset =>
        Math.Max(WindowDecorationMargin.Right, OperatingSystem.IsWindows() ? 144 : 0) + 12;

    private void InitializeTitleActions()
    {
        _titleBarLogo = AppLogo();
        _titleBarLogo.Name = "TitleBarLogo";
        _titleBarLogo.Width = _titleBarLogo.Height = TitleLogoSize;
        _titleBarLogo.HorizontalAlignment = HorizontalAlignment.Left;
        _titleBarLogo.VerticalAlignment = VerticalAlignment.Center;
        _titleBarLogo.Margin = new Thickness(0, 0, TitleLogoGap, 0);
        _titleBarLogo.IsHitTestVisible = false;
        Avalonia.Automation.AutomationProperties.SetName(_titleBarLogo, "Wandur");
        _plaqueIdentity = new Grid
        {
            Name = "PlaqueIdentity", ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Children = { _titleBarLogo }
        };
        _themeMenuButton = new ThemeMenuButton(() => Controller);
        _skinMenuButton = new SkinMenuButton(() => Controller);
        var fullScreen = ToolbarButton(FullScreenGlyph, "TitleFullScreenButton", nameof(Strings.FullScreen));
        fullScreen.Width = fullScreen.Height = 30;
        fullScreen.Click += (_, _) => ToggleFullScreen();
        _titleActions = new StackPanel
        {
            Name = "TitleActions", Orientation = Orientation.Horizontal, Spacing = 4,
            Width = TitleActionsWidth, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Children = { _skinMenuButton, _themeMenuButton, fullScreen }
        };
        WindowDecorationProperties.SetElementRole(_titleActions, WindowDecorationsElementRole.User);
        _chrome.Children.Add(_titleActions);
        PositionTitleActions();
        _exitFullScreenButton = ToolbarButton(ExitFullScreenGlyph, "ExitFullScreenButton", nameof(Strings.ExitFullScreen));
        _exitFullScreenButton.Width = _exitFullScreenButton.Height = 28;
        _exitFullScreenButton.IsVisible = false;
        _exitFullScreenButton.Click += (_, _) => ToggleFullScreen();
        _toolbarActions.Children.Add(_exitFullScreenButton);
    }

    private void PositionTitleActions()
    {
        var custom = ThemeService.ActiveWindowSkin.CustomChrome;
        Panel target = custom ? _chrome : _toolbarActions;
        if (!ReferenceEquals(_titleActions.Parent, target))
        {
            if (_titleActions.Parent is Panel old) old.Children.Remove(_titleActions);
            target.Children.Add(_titleActions);
        }
        _titleActions.VerticalAlignment = custom ? VerticalAlignment.Top : VerticalAlignment.Center;
        // Centered in the band: Armored keeps its 10 DIP top, Fleet derives it from its band height.
        var top = ThemeService.ActiveWindowSkin.IsArmored ? 10 : FleetTitleLayout.ActionsTop;
        _titleActions.Margin = custom ? new Thickness(0, top, TitleActionsRightInset, 0) : new Thickness(6, 0, 0, 0);
    }

    internal void ToggleFullScreen()
    {
        if (_closed || _fullScreenRequest.HasValue) return;
        var target = WindowState == WindowState.FullScreen ? _beforeFullScreen : WindowState.FullScreen;
        _fullScreenRequest = target;
        try
        {
            if (target == WindowState.FullScreen)
            {
                // Prepare the content before AppKit/Win32 starts its window transition,
                // rather than leaving the old title and frame for a later dispatcher pass.
                ApplyFullScreenChrome();
                UpdateLayout();
            }
            WindowState = target;
        }
        finally
        {
            _fullScreenRequest = null;
            // Reconcile with the actual reported state, including a refused request.
            // Native setters can notify synchronously, so keep restoration deferred.
            RequestTitleChromeUpdate();
        }
    }

    private void ApplyFullScreenChrome()
    {
        if (_fullScreenChrome && !_fullScreenChromeDirty)
        {
            UpdateTitleBarInsets();
            PlaceFullScreenExit();
            return;
        }
        _fullScreenChrome = true;
        _fullScreenChromeDirty = false;
        _themeMenuButton.Flyout?.Hide();
        _skinMenuButton.Flyout?.Hide();
        _titleActions.IsVisible = false;
        _titleBarLogo.IsVisible = false;
        _plaqueTitleHost.IsVisible = false;
        _metalDrag.IsVisible = false;
        _titleBarIdentity.IsVisible = false;
        _appTitle.IsVisible = false;
        _ornaments.IsVisible = false;
        WindowDecorationProperties.SetElementRole(_windowHeader, WindowDecorationsElementRole.User);
        // Keep content and its theme intact, but release all decorative title/frame insets.
        // The next windowed pass reloads these values from the current theme, not an old snapshot.
        _windowSkin.BandHeight = 0;
        _windowSkin.EdgeThickness = 0;
        _windowSkin.BorderBitmap = null;
        _windowSkin.Inset = default;
        _windowSkin.TitleModuleBounds = default;
        _bezel.BorderBitmap = null;
        _bezel.Inset = default;
        ApplyToolbarSlot(null);
        UpdateTitleBarInsets();
        ExtendClientAreaTitleBarHeightHint = 0;
        BindToolbarBackground();
        PlaceFullScreenExit();
    }

    private void PlaceFullScreenExit()
    {
        // View > Hide Toolbar must not strand mouse users in fullscreen. Reuse the
        // existing footer row in that case, without adding another row of chrome.
        if (_footer.Child is not Grid footer) return;
        Panel target = ToolbarVisible ? _toolbarActions : footer;
        if (!ReferenceEquals(_exitFullScreenButton.Parent, target))
        {
            if (_exitFullScreenButton.Parent is Panel old) old.Children.Remove(_exitFullScreenButton);
            if (ReferenceEquals(target, footer))
            {
                if (footer.ColumnDefinitions.Count == 2) footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                Grid.SetColumn(_exitFullScreenButton, 2);
            }
            target.Children.Add(_exitFullScreenButton);
        }
        _exitFullScreenButton.IsVisible = true;
    }

    private void RestoreWindowedChrome()
    {
        if (!_fullScreenChrome) return;
        _fullScreenChrome = false;
        _windowSkin.ApplyFromTheme();
        _bezel.ApplyFromTheme();
        _ornaments.ApplyFromTheme();
        _ornaments.IsVisible = true;
        _titleActions.IsVisible = true;
        _appTitle.IsVisible = true;
        _exitFullScreenButton.IsVisible = false;
        _fleetToolbarSurfaceKey = null;
        BindToolbarBackground();
    }
}
