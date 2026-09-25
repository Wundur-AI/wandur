using System.Text.Json;
using Avalonia.Headless.XUnit;
using Wandur.Core.Settings;

namespace Wandur.Desktop.Tests;

public sealed class WindowSkinSelectionTests
{
    [AvaloniaFact]
    public async Task CancelAfterWorldChangeRetainsCurrentWorldDraftAndConnection()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "wandur-skin-preview-" + Guid.NewGuid(), "settings.json"));
        store.Save(new() { Theme = "Slate" });
        var window = new MainWindow(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store,
            new MemoryPasswordVault(), new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore());
        try
        {
            window.Show();
            await window.Controller.StartAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var input = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                .OfType<Avalonia.Controls.TextBox>().Single(b => b.Name == "CommandInput");
            input.Text = "keep my draft";
            var transcript = window.Controller.Terminal.PlainText;
            using (var model = new Wandur.Desktop.ViewModels.PreferencesViewModel(window.Controller, window.Sessions.PreviewAppearanceSettings))
            {
                model.Skin = "Armored";
                window.Controller.StageWorldTheme(UserTheme.FromPreset("Paper").ToWorldTheme());
                window.Sessions.ApplyAppearance();
                Assert.Equal("Armored", ThemeService.ActiveWindowSkin.Id);
            }
            window.Sessions.EndAppearanceSettingsPreview();
            Assert.Equal("Fleet", ThemeService.ActiveWindowSkin.Id);
            Assert.Equal(Avalonia.Media.Colors.White,
                ((Avalonia.Media.ISolidColorBrush)Avalonia.Application.Current!.Resources["TerminalBrush"]!).Color);
            Assert.True(window.Controller.IsConnected);
            Assert.Equal("keep my draft", input.Text);
            Assert.Equal(transcript, window.Controller.Terminal.PlainText);
            var view = Avalonia.Controls.NativeMenu.GetMenu(window)!.Items.OfType<Avalonia.Controls.NativeMenuItem>()
                .Single(i => i.Header == Wandur.Core.Localization.Strings.View).Menu!;
            var skins = view.Items.OfType<Avalonia.Controls.NativeMenuItem>().Single(i => i.Header == Wandur.Core.Localization.Strings.Skin).Menu!;
            var system = skins.Items.OfType<Avalonia.Controls.NativeMenuItem>().Single(i => i.Header == Wandur.Core.Localization.Strings.SkinSystem);
            system.Command!.Execute(null);
            Assert.Equal("System", window.Controller.Settings.Skin);
        }
        finally { await window.Sessions.DisposeAsync(); window.Close(); ThemeService.Apply(new()); }
    }

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
