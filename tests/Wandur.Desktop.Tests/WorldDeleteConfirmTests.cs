using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Views;

namespace Wandur.Desktop.Tests;

/// <summary>
/// Deleting a saved world throws away its login, its protocol mapping and its scripts. One stray click on
/// the toolbar button the panel used to have was enough to lose all of it with nothing to undo it, which is
/// exactly how it was lost; the Delete key and the row menu ask first.
/// </summary>
public sealed class WorldDeleteConfirmTests
{
    private static async Task<(SessionWorkspace Sessions, WorldLibraryView View, string Path)> OpenAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-delete-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings
        {
            Profiles =
            [
                new ConnectionProfile { Name = "Keep Me", Host = "keep.test", Port = 4000 },
                new ConnectionProfile { Name = "Legends of the Jedi", Host = "legendsofthejedi.com", Port = 5656 },
            ],
        });
        var window = new MainWindow(new Wandur.Desktop.Terminal.TranscriptDisplayFactory(), store,
            new MemoryPasswordVault(), new MemoryRoomMapStore(), new RecordingScriptFactory(),
            new MemoryScriptLibraryStore());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        var view = window.GetVisualDescendants().OfType<WorldLibraryView>().First();
        return (window.Sessions, view, path);
    }

    [AvaloniaFact]
    public async Task TheDeleteKeyAsksBeforeItRemovesAnything()
    {
        var (sessions, view, path) = await OpenAsync();
        try
        {
            var model = (ViewModels.WorldLibraryViewModel)view.DataContext!;
            model.SelectedProfile = model.Profiles.Single(p => p.Name == "Legends of the Jedi");
            Dispatcher.UIThread.RunJobs();

            var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "WorldProfiles");
            list.ContainerFromItem(model.SelectedProfile!)!.Focus();
            var window = (Window)TopLevel.GetTopLevel(view)!;
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            // Asked, not done.
            Assert.True(model.ConfirmDelete);
            Assert.Equal("Legends of the Jedi", model.PendingDeleteName);
            Assert.Equal(2, sessions.Active.Controller.Settings.Profiles.Count);

            var confirm = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DeleteWorldConfirm");
            Assert.True(confirm.IsVisible);
        }
        finally { try { Directory.Delete(path, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task CancellingKeepsTheWorld()
    {
        var (sessions, view, path) = await OpenAsync();
        try
        {
            var model = (ViewModels.WorldLibraryViewModel)view.DataContext!;
            model.SelectedProfile = model.Profiles.Single(p => p.Name == "Legends of the Jedi");
            model.RequestDeleteCommand.Execute(null);
            model.CancelDeleteCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(model.ConfirmDelete);
            Assert.Equal(2, sessions.Active.Controller.Settings.Profiles.Count);

            // And the confirm button does nothing on its own once the prompt is gone.
            await model.DeleteCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, sessions.Active.Controller.Settings.Profiles.Count);
        }
        finally { try { Directory.Delete(path, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task ConfirmingRemovesOnlyThatWorld()
    {
        var (sessions, view, path) = await OpenAsync();
        try
        {
            var model = (ViewModels.WorldLibraryViewModel)view.DataContext!;
            model.SelectedProfile = model.Profiles.Single(p => p.Name == "Legends of the Jedi");
            model.RequestDeleteCommand.Execute(null);
            await model.DeleteCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            var left = sessions.Active.Controller.Settings.Profiles;
            Assert.Equal("Keep Me", Assert.Single(left).Name);
            Assert.False(model.ConfirmDelete);
        }
        finally { try { Directory.Delete(path, true); } catch (IOException) { } }
    }

    /// <summary>
    /// The prompt and the deletion both read the world the prompt was raised for, never the selection, so
    /// a row right-clicked and then deselected still removes the world the reader was shown.
    /// </summary>
    [AvaloniaFact]
    public async Task ThePromptRemovesTheWorldItNamesEvenIfTheSelectionMoves()
    {
        var (sessions, view, path) = await OpenAsync();
        try
        {
            var model = (ViewModels.WorldLibraryViewModel)view.DataContext!;
            var doomed = model.Profiles.Single(p => p.Name == "Legends of the Jedi");
            model.RequestDeleteProfileCommand.Execute(doomed);
            Assert.Equal("Legends of the Jedi", model.PendingDeleteName);

            model.SelectedProfile = model.Profiles.Single(p => p.Name == "Keep Me");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Legends of the Jedi", model.PendingDeleteName);

            await model.DeleteCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Keep Me", Assert.Single(sessions.Active.Controller.Settings.Profiles).Name);
        }
        finally { try { Directory.Delete(path, true); } catch (IOException) { } }
    }
}
