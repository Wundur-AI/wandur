using Avalonia.Controls;
using Dock.Avalonia.Controls;

namespace Wandur.Desktop;

/// <summary>Keeps Dock's live content and native caption controls while applying compact panel chrome.</summary>
internal sealed class SkinnedDockHostWindow : HostWindow
{
    public SkinnedDockHostWindow()
    {
        Opened += (_, _) =>
        {
            // Hide/Show raises Opened again on the same live host.
            ThemeService.Applied -= ApplySkin;
            ThemeService.Applied += ApplySkin;
            ApplySkin();
        };
        Closed += (_, _) => ThemeService.Applied -= ApplySkin;
        ApplySkin();
    }

    private void ApplySkin()
    {
        var skin = ThemeService.ActiveWindowSkin;
        Classes.Set("skin-fleet", skin.Id == "Fleet");
        Classes.Set("skin-armored", skin.IsArmored);
        Classes.Set("skin-system", !skin.CustomChrome);
        Classes.Set("fleet", skin.CustomChrome);
        Classes.Set("skin-controls", true);
        // Do not let Dock's single-tool pseudo-class remove native caption buttons.
        // The native caption remains compact; the live Dock header uses our shared palette.
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = false;
        ExtendClientAreaTitleBarHeightHint = -1;
    }
}
