using Avalonia.Headless.XUnit;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

public sealed class SystemSkinGroundTests
{
    [AvaloniaTheory]
    [InlineData("Linen")]
    [InlineData("Paper")]
    [InlineData("Midnight")]
    [InlineData("Hull")]
    public void SystemSkinGapsUseTheThemeBorderColourNotTheDarkChassis(string theme)
    {
        try
        {
            ThemeService.Apply(new ClientSettings { Theme = theme, Skin = WindowSkinId.System, UseWorldThemes = false });
            var border = UserTheme.FromPreset(theme).Colors["Border"];
            var ground = ThemeService.AppliedSkin!.Surfaces!.Ground!;
            Assert.Equal(border, ground.From);
            Assert.Equal(border, ground.To);
        }
        finally { ThemeService.Apply(new ClientSettings()); }
    }

    [AvaloniaFact]
    public void DrawnSkinsKeepTheirChassis()
    {
        try
        {
            ThemeService.Apply(new ClientSettings { Theme = "Linen", Skin = WindowSkinId.Fleet, UseWorldThemes = false });
            Assert.NotEqual(UserTheme.FromPreset("Linen").Colors["Border"], ThemeService.AppliedSkin!.Surfaces!.Ground!.From);
        }
        finally { ThemeService.Apply(new ClientSettings()); }
    }
}
