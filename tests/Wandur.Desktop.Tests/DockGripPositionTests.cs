using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace Wandur.Desktop.Tests;

/// <summary>
/// System and Fleet put the six-dot grip at the far left of a panel header: its box starts 5 DIP from the panel edge
/// and draws its dots from about 7, the title follows, and grip, title and buttons share one centre line. The grip
/// stays visible and keeps its move cursor. Armored keeps its grip where it was.
/// </summary>
public sealed class DockGripPositionTests
{
    [AvaloniaTheory]
    [InlineData("System", "Linen", 5, 23)]
    [InlineData("System", "Midnight", 5, 23)]
    [InlineData("Fleet", "Hull", 5, 23)]
    [InlineData("Fleet", "Ember", 5, 23)]
    [InlineData("Armored", "Hull", 20, 38)]
    public async Task TheGripSitsInTheLeftInset(string skin, string theme, double grip, double title)
    {
        var window = WindowSkinTransitionTests.Create();
        try
        {
            window.Show(); WindowSkinTransitionTests.Settle(window);
            window.Sessions.PreviewAppearanceSettings(new() { Skin = skin, Theme = theme });
            WindowSkinTransitionTests.Settle(window);
            // Hit testing reads the rendered scene.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            WindowSkinTransitionTests.Settle(window);
            var chromes = window.GetVisualDescendants().OfType<ToolChromeControl>().Where(c => c.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(chromes);
            foreach (var chrome in chromes)
            {
                var header = chrome.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grip");
                Rect In(Control control) => new(control.TranslatePoint(default, header)!.Value, control.Bounds.Size);
                var gripControl = chrome.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");
                var glyph = In(gripControl);
                var text = In(chrome.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title"));
                var close = In(chrome.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CloseButton"));
                Assert.Equal(grip, glyph.Left);
                Assert.Equal(title, text.Left);
                Assert.True(glyph.Right < text.Left, "the title starts after the grip");
                // One centre line, to within half a DIP (an odd text height rounds its centre by half).
                Assert.InRange(Math.Abs(header.Bounds.Height / 2 - glyph.Center.Y), 0, .5);
                Assert.InRange(Math.Abs(glyph.Center.Y - text.Center.Y), 0, .5);
                Assert.InRange(Math.Abs(glyph.Center.Y - close.Center.Y), 0, .5);
                // Visible, and the pointer over its dots still finds it (the move cursor and the drag start there).
                Assert.True(gripControl.IsEffectivelyVisible);
                Assert.Equal(1, gripControl.Opacity);
                var centre = header.TranslatePoint(glyph.Center, window)!.Value;
                var hit = window.InputHitTest(centre) as Visual;
                Assert.True(hit is not null && (ReferenceEquals(hit, gripControl) || hit.GetVisualAncestors().Contains(gripControl)),
                    $"the grip is not hit at {centre}: {hit?.GetType().Name} {(hit as Control)?.Name}");
            }
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }
}
