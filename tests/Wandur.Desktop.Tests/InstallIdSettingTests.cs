using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;
using Wandur.Core.Storage;
using Wandur.Desktop.Terminal;
using Wandur.Desktop.ViewModels;
using Wandur.Desktop.Views;
using L = Wandur.Core.Localization.Strings;

namespace Wandur.Desktop.Tests;

/// <summary>The anonymous install id setting, from the General page to the header the directory receives. The directory
/// here is a loopback server; nothing leaves the machine.</summary>
[Collection(UiLanguageCollection.Name)]
public sealed class InstallIdSettingTests
{
    [AvaloniaFact]
    public async Task TheSettingSitsUnderUpdateChecksAndTurnsTheHeaderOff()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wandur-install-setting-" + Guid.NewGuid());
        using var site = new Site();
        var database = new ClientDatabase(Path.Combine(directory, "test.db"));
        var store = new SqliteSettingsStore(database, Path.Combine(directory, "legacy.json"));
        var catalog = new WorldCatalog(Path.Combine(directory, "directory.json"), site.Base);
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(), new MemoryRoomMapStore(),
            new RecordingScriptFactory(), new MemoryScriptLibraryStore(), catalog: catalog);
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var id = window.Controller.Settings.InstallId;
            Assert.NotNull(id);
            Assert.True(window.Controller.Settings.SendInstallId);
            Assert.Equal(id!.Value.ToString("D"), catalog.Install.Value);
            Assert.Contains($"\r\n{InstallIdentity.Header}: {id:D}\r\n", await site.LoadAsync(catalog));

            using var model = new PreferencesViewModel(window.Controller, window.Sessions.PreviewAppearanceSettings);
            var dialog = new OptionsDialog(model); dialog.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var updates = dialog.FindControl<CheckBox>("CheckForUpdates")!;
                var install = dialog.FindControl<CheckBox>("SendInstallId")!;
                var panel = Assert.IsType<StackPanel>(install.Parent);
                Assert.Same(panel, updates.Parent);
                // Right after the update check's own hint.
                Assert.Equal(panel.Children.IndexOf(updates) + 2, panel.Children.IndexOf(install));
                Assert.Equal(L.SendInstallId, install.Content);
                Assert.True(install.IsChecked);
                install.IsChecked = false; Dispatcher.UIThread.RunJobs();
                model.SaveCommand.Execute(null);
            }
            finally { dialog.Close(); }

            Assert.False(window.Controller.Settings.SendInstallId);
            Assert.Equal(id, window.Controller.Settings.InstallId);
            Assert.Null(catalog.Install.Value);
            var off = await site.LoadAsync(catalog);
            Assert.DoesNotContain(InstallIdentity.Header, off, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(id.Value.ToString("D"), off);

            // Saved: the next start finds the setting off and the same id.
            var reloaded = store.Load().Settings;
            Assert.False(reloaded.SendInstallId);
            Assert.Equal(id, reloaded.InstallId);
        }
        finally
        {
            await window.Sessions.DisposeAsync(); window.Close();
            catalog.Dispose();
            ThemeService.Apply(new());
            database.Dispose();
            TestFiles.DeleteDirectory(directory);
        }
    }

    /// <summary>A loopback directory that answers every request with an empty snapshot and keeps the heads it saw.</summary>
    private sealed class Site : IDisposable
    {
        private const string Body = """{"schema_version":1,"fetched_at":"2026-10-04T00:00:00Z","games":[]}""";
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly ConcurrentQueue<string> _requests = new();
        private readonly CancellationTokenSource _stop = new();
        public Uri Base { get; }

        public Site()
        {
            _listener.Start();
            Base = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (Exception) { return; }
                    using (client)
                    {
                        var stream = client.GetStream();
                        var head = new StringBuilder();
                        var buffer = new byte[4096];
                        while (!head.ToString().Contains("\r\n\r\n"))
                        {
                            var read = await stream.ReadAsync(buffer);
                            if (read == 0) break;
                            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        }
                        _requests.Enqueue(head.ToString());
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Body.Length}\r\nConnection: close\r\n\r\n{Body}"));
                    }
                }
            });
        }

        /// <summary>Loads the directory and returns the head of the last directory request the server saw.</summary>
        public async Task<string> LoadAsync(WorldCatalog catalog)
        {
            await catalog.LoadAsync(force: true);
            return _requests.Where(request => request.StartsWith("GET /directory ", StringComparison.Ordinal)).Last();
        }

        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
