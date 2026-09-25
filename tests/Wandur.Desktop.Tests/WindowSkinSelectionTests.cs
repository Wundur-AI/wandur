using System.Text.Json;
using Avalonia.Headless.XUnit;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

public sealed class WindowSkinSelectionTests
{
    [AvaloniaTheory]
    [InlineData("Fleet", 50)]
    [InlineData("Armored", 64)]
    [InlineData("System", 0)]
    public void WorldColorsCannotReplaceUserGeometry(string id, double height)
    {
        var world = UserTheme.FromPreset("Paper").ToWorldTheme();
        var before = WindowSkinDefinition.Resolve(id);
        try
        {
            ThemeService.Apply(new ClientSettings { Skin = id }, world);
            Assert.Equal(before, ThemeService.ActiveWindowSkin);
            Assert.Equal(height, ThemeService.AppliedSkin!.Layout!.TitleBar!.Height);
            Assert.Equal(Avalonia.Media.Color.Parse(world.Colors.Terminal),
                ((Avalonia.Media.ISolidColorBrush)Avalonia.Application.Current!.Resources["TerminalBrush"]!).Color);
        }
        finally { ThemeService.Apply(new()); }
    }

    [AvaloniaFact]
    public void SkinOnlyChangeRepaintsOnceAndDoesNotChangeTerminalColors()
    {
        var fleet = new ClientSettings { Theme = "Slate" };
        ThemeService.Apply(fleet);
        var terminal = Avalonia.Application.Current!.Resources["TerminalBrush"]!.ToString();
        var paints = 0;
        void Changed() => paints++;
        ThemeService.Applied += Changed;
        try
        {
            var armored = JsonSerializer.Deserialize<ClientSettings>("{\"Skin\":\"Armored\",\"Theme\":\"Slate\"}")!;
            ThemeService.Apply(armored);
            Assert.Equal(64, ThemeService.AppliedSkin!.Layout!.TitleBar!.Height);
            Assert.Equal(terminal, Avalonia.Application.Current.Resources["TerminalBrush"]!.ToString());
            ThemeService.Apply(armored);
            Assert.Equal(1, paints);
        }
        finally { ThemeService.Applied -= Changed; ThemeService.Apply(fleet); }
    }
}
