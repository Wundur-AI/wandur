using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// The System skin's panel headers are flat: one colour, no bevel, gloss or grain, and a single 1 DIP hairline in the
/// theme's line colour under them.
/// </summary>
public sealed class SystemFlatHeaderTests
{
    private static readonly UserTheme Custom = UserTheme.FromPreset("Ember") with
    {
        Id = "custom-flat-headers", Name = "Flat headers",
        Colors = new(UserTheme.FromPreset("Ember").Colors) { ["Chrome"] = "#3A2F4A", ["Panel"] = "#2B2236", ["Border"] = "#5A4A6E" }
    };

    [AvaloniaTheory]
    [InlineData("Linen")]
    [InlineData("Paper")]
    [InlineData("Midnight")]
    [InlineData("Ember")]
    [InlineData("custom-flat-headers")]
    public async Task SystemHeadersAreThePanelColourOverAHairline(string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            window.Sessions.PreviewAppearanceSettings(new() { Skin = "System", Theme = theme, CustomThemes = [Custom] });
            WindowSkinTransitionTests.Settle(window);

            var surface = ThemeService.AppliedSkin!.Surfaces!.PanelHeader!;
            Assert.Equal("none", surface.Bevel);
            Assert.Equal(0, surface.Gloss);
            Assert.Equal(0, surface.Grain);
            Assert.Equal(surface.From, surface.To);

            var colors = theme == Custom.Id ? Custom.Colors : UserTheme.FromPreset(theme).Colors;
            var face = Color.Parse(theme == Custom.Id ? Custom.Colors["Chrome"] : colors["Panel"]);
            Assert.Equal(face, Color.Parse(surface.From));
            var line = Color.Parse(colors["Border"]);
            AssertFlat(window, face, line);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

    internal static void AssertFlat(Window window, Color face, Color line)
    {
        var chromes = window.GetVisualDescendants().OfType<ToolChromeControl>().Where(c => c.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(chromes);
        foreach (var chrome in chromes)
        {
            var header = chrome.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip");
            Assert.Equal(face, SolidColor(header.Background));
            var border = chrome.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Name == "PART_Border" && ReferenceEquals(b.TemplatedParent, chrome));
            Assert.Equal(new Thickness(0, 0, 0, 1), border.BorderThickness);
            Assert.Equal(line, SolidColor(border.BorderBrush));
            foreach (var shadow in border.BoxShadow) Assert.Equal(0, shadow.Color.A);
        }
    }

    /// <summary>A solid brush's colour, or a gradient's when every stop is the same colour (a flat surface).</summary>
    internal static Color SolidColor(IBrush? brush)
    {
        switch (brush)
        {
            case ISolidColorBrush solid: return solid.Color;
            case IGradientBrush gradient:
                var colors = gradient.GradientStops.Select(stop => stop.Color).Distinct().ToArray();
                Assert.True(colors.Length == 1, "The header is shaded: " + string.Join(", ", colors));
                return colors[0];
            default: throw new Xunit.Sdk.XunitException($"Unexpected header brush {brush?.GetType().Name ?? "null"}.");
        }
    }
}
