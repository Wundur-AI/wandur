using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Wandur.Core.Settings;
using Wandur.Desktop.Terminal;

namespace Wandur.Desktop.Tests;

public sealed class TypedWorldAddressTests
{
    [Theory]
    [InlineData("mud.example.org:4000", "mud.example.org", 4000, false)]
    [InlineData("  mud.example.org 4000 ", "mud.example.org", 4000, false)]
    [InlineData("telnet://mud.example.org:23", "mud.example.org", 23, false)]
    [InlineData("tls://mud.example.org:4443", "mud.example.org", 4443, true)]
    [InlineData("127.0.0.1:4410", "127.0.0.1", 4410, false)]
    public void ATypedAddressBecomesAnUnsavedWorld(string text, string host, int port, bool tls)
    {
        var profile = Assert.IsType<ConnectionProfile>(MainWindow.TypedProfile(text, []));
        Assert.Equal((host, port, tls), (profile.Host, profile.Port, profile.UseTls));
        Assert.Equal($"{host}:{port}", profile.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("mud.example.org")]
    [InlineData("Legends of the Jedi")]
    [InlineData("mud.example.org:99999")]
    [InlineData("https://mud.example.org:4000")]
    public void TextThatIsNotAnAddressWithAPortConnectsNowhere(string text) => Assert.Null(MainWindow.TypedProfile(text, []));

    [Fact]
    public void ATypedAddressThatMatchesASavedWorldOpensThatWorld()
    {
        var saved = new ConnectionProfile { Name = "Aardwolf", Host = "aardmud.org", Port = 4000 };
        Assert.Same(saved, MainWindow.TypedProfile("AARDMUD.org:4000", [saved]));
        Assert.NotSame(saved, MainWindow.TypedProfile("tls://aardmud.org:4000", [saved]));
    }

    [AvaloniaFact]
    public async Task TypingAnAddressInThePickerAndPressingEnterOpensASession()
    {
        var path = Path.Combine(Path.GetTempPath(), "wandur-typed-address-" + Guid.NewGuid());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var store = new SettingsStore(Path.Combine(path, "settings.json"));
        store.Save(new ClientSettings { UseWorldThemes = false, Profiles = [new ConnectionProfile { Name = "Saved", Host = "127.0.0.1", Port = 1 }] });
        var window = new MainWindow(new TranscriptDisplayFactory(), store, new MemoryPasswordVault(),
            new MemoryRoomMapStore(), new RecordingScriptFactory(), new MemoryScriptLibraryStore()) { Width = 1300, Height = 820 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var picker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ToolbarWorlds");
            Assert.True(picker.IsEditable);
            Assert.Equal("Saved", picker.Text);
            picker.Text = $"127.0.0.1:{port}";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(port, window.SelectedProfile!.Port);
            var accept = listener.AcceptTcpClientAsync();
            picker.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = picker });
            using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.Sessions.Tabs, tab => tab.Profile?.Port == port);
            Assert.DoesNotContain(window.Controller.Settings.Profiles, p => p.Port == port);
        }
        finally { window.Close(); try { Directory.Delete(path, true); } catch { } }
    }
}
