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
        ExtendClientAreaToDecorationsHint = custom;
        _titleActions.IsVisible = custom;
        _appTitle.IsVisible = custom;
        _ornaments.IsVisible = custom;
        if (custom) return;

        _themeMenuButton.Flyout?.Hide();
        _skinMenuButton.Flyout?.Hide();
        _skinTitleActive = false;
        _titleBarLogo.IsVisible = false;
        _plaqueTitleHost.IsVisible = false;
        _titleBarIdentity.IsVisible = false;
        _metalDrag.IsVisible = false;
        _windowSkin.TitleModuleBounds = default;
        ExtendClientAreaTitleBarHeightHint = -1;
        ApplyToolbarSlot(null);
        _headerStack.Margin = default;
        _windowHeader.MinHeight = 0;
        WindowDecorationProperties.SetElementRole(_metalDrag, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(_plaqueTitleHost, WindowDecorationsElementRole.User);
        UpdateTitleBarInsets();
    }
}
