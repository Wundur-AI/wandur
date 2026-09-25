using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;

namespace Wandur.Desktop;

public sealed partial class MainWindow
{
    private void ApplyWindowSkin()
    {
        Classes.Set("skin-controls", true);
        var custom = ThemeService.ActiveWindowSkin.CustomChrome;
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
        _titleActions.IsVisible = true;
        PositionTitleActions();
        _appTitle.IsVisible = custom;
        _ornaments.IsVisible = custom;
        WindowDecorationProperties.SetElementRole(_windowHeader,
            custom ? WindowDecorationsElementRole.User : WindowDecorationsElementRole.TitleBar);
        // The Windows menu row is inside this header, but must never become caption space.
        WindowDecorationProperties.SetElementRole(_menus.Fallback, WindowDecorationsElementRole.User);
        if (custom) return;

        _themeMenuButton.Flyout?.Hide();
        _skinMenuButton.Flyout?.Hide();
        _skinTitleActive = false;
        _titleBarLogo.IsVisible = false;
        _plaqueTitleHost.IsVisible = false;
        if (_appTitle.Parent == _plaqueIdentity) _plaqueIdentity.Children.Remove(_appTitle);
        if (_appTitle.Parent is null) _titleBarIdentity.Children.Add(_appTitle);
        _appTitle.IsVisible = true;
        _appTitle.FontSize = 13;
        _appTitle.Bind(TextBlock.ForegroundProperty,
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("TextBrush"));
        _appTitle.LetterSpacing = 0;
        _appTitle.MaxWidth = 260;
        _appTitle.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        _titleBarIdentity.IsVisible = true;
        _metalDrag.IsVisible = false;
        _windowSkin.TitleModuleBounds = default;
        ExtendClientAreaTitleBarHeightHint = 48;
        ApplyToolbarSlot(null);
        _headerStack.Margin = default;
        _windowHeader.MinHeight = 0;
        WindowDecorationProperties.SetElementRole(_metalDrag, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(_plaqueTitleHost, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(_toolbarActions, WindowDecorationsElementRole.User);
        UpdateTitleBarInsets();
        // Reserve actual control width before allocating space to a long world name.
        _toolbarActions.Measure(new Size(double.PositiveInfinity, 48));
        var width = _chrome.Bounds.Width > 0 ? _chrome.Bounds.Width : Width;
        _appTitle.MaxWidth = Math.Clamp(width - _toolbar.Padding.Left - _toolbar.Padding.Right
            - _toolbarActions.DesiredSize.Width - 88, 24, 260);
    }
}
