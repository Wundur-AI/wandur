using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

/// <summary>
/// Fleet's panel headers are as short as System's (30 DIP) and flat: one colour, no bevel, gloss, grain or top
/// highlight, over Fleet's rim edge. The title row fills the header so descenders are not clipped.
/// </summary>
public sealed class FleetFlatHeaderTests
{
    private static readonly UserTheme Custom = UserTheme.FromPreset("Ember") with
    {
        Id = "custom-fleet-headers", Name = "Fleet headers",
        Colors = new(UserTheme.FromPreset("Ember").Colors) { ["Chrome"] = "#3A2F4A", ["Panel"] = "#2B2236", ["Border"] = "#5A4A6E" }
    };

    [AvaloniaTheory]
    [InlineData("Hull")]
    [InlineData("Ember")]
    [InlineData("Linen")]
    [InlineData("custom-fleet-headers")]
    public async Task FleetHeadersAreShortAndFlat(string theme)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            window.Sessions.PreviewAppearanceSettings(new() { Skin = "Fleet", Theme = theme, CustomThemes = [Custom] });
            WindowSkinTransitionTests.Settle(window);

            var skin = ThemeService.AppliedSkin!;
            Assert.Equal(30, skin.Layout!.PanelHeader!.Height);
            var surface = skin.Surfaces!.PanelHeader!;
            Assert.Equal("none", surface.Bevel);
            Assert.Equal(0, surface.Gloss);
            Assert.Equal(0, surface.Grain);
            Assert.Equal(surface.From, surface.To);
            if (theme == Custom.Id) Assert.Equal(Color.Parse(Custom.Colors["Chrome"]), Color.Parse(surface.From));
            Assert.Equal(0, ((BoxShadows)Application.Current!.Resources["FleetHeaderShadow"]!).Count);

            var face = Color.Parse(surface.From);
            var rim = SolidColor((IBrush)Application.Current.Resources["FleetRimEdgeBrush"]!);
            var chromes = window.GetVisualDescendants().OfType<ToolChromeControl>().Where(c => c.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(chromes);
            foreach (var chrome in chromes)
            {
                var header = chrome.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip");
                Assert.Equal(30, header.Bounds.Height);
                Assert.Equal(face, SolidColor(header.Background));
                var border = chrome.GetVisualDescendants().OfType<Border>()
                    .Single(b => b.Name == "PART_Border" && ReferenceEquals(b.TemplatedParent, chrome));
                Assert.Equal(new Thickness(0, 0, 0, 1), border.BorderThickness);
                Assert.Equal(rim, SolidColor(border.BorderBrush));
                foreach (var shadow in border.BoxShadow) Assert.Equal(0, shadow.Color.A);
                var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
                Assert.True(title.Bounds.Height >= 16, $"the title row is {title.Bounds.Height} DIP tall");
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
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
