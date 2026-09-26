using Wandur.Core.Settings;
using Wandur.Core.Sessions;

namespace Wandur.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wandur-tests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void SettingsAndWorldProfilesSurviveRestart()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new() { Theme = "Forest", FontSize = 18, Profiles = [new() { Name = "Test world", Host = "localhost", Port = 1234 }] });
        var loaded = new SettingsStore(FilePath).Load();
        Assert.Null(loaded.Warning);
        Assert.Equal("Forest", loaded.Settings.Theme);
        Assert.Equal(18, loaded.Settings.FontSize);
        Assert.Equal("localhost", Assert.Single(loaded.Settings.Profiles).Host);
    }

    [Fact]
    public void CorruptSettingsWarnAndArePreservedBeforeNextSave()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{broken");
        var store = new SettingsStore(FilePath);
        Assert.NotNull(store.Load().Warning);
        Assert.Equal("{broken", File.ReadAllText(FilePath));
        store.Save(new());
        Assert.Contains(Directory.GetFiles(_directory), p => p.Contains(".corrupt-") && File.ReadAllText(p) == "{broken");
        Assert.Null(new SettingsStore(FilePath).Load().Warning);
    }

    [Theory]
    [InlineData("", 4000)]
    [InlineData("mud host", 4000)]
    [InlineData("https://mud.example", 4000)]
    [InlineData("localhost", 0)]
    [InlineData("localhost", 65536)]
    public void InvalidEndpointsAreRejectedBeforeConnecting(string host, int port)
    {
        Assert.Throws<ArgumentException>(() => new ConnectionProfile { Host = host, Port = port }.Validate());
    }

    [Fact]
    public void InvalidAppearanceCannotReplaceGoodSettings()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new());
        Assert.Throws<ArgumentException>(() => store.Save(new() { Background = "not a color" }));
        Assert.Throws<ArgumentException>(() => store.Save(new() { FontSize = double.NaN }));
        Assert.Null(store.Load().Settings.Background);
    }

    [Fact]
    public void SavedDefaultPasswordPatternRecognizesLotjWithoutChangingCustomPatterns()
    {
        const string previousDefault = @"^\s*(?:(?:please\s+)?enter\s+(?:your\s+)?|your\s+)?(?:password|passphrase|passcode)\s*[:>]\s*$";
        var oldProfile = new ConnectionProfile
        {
            Host = "localhost", Username = "player", PasswordId = Guid.NewGuid(), AutoLogin = true,
            PasswordPrompt = previousDefault
        };
        var customProfile = new ConnectionProfile { Host = "localhost", PasswordPrompt = @"^Secret\?$" };
        var store = new SettingsStore(FilePath);
        store.Save(new() { Profiles = [oldProfile, customProfile] });

        var loaded = store.Load();
        Assert.Null(loaded.Warning);
        var profile = loaded.Settings.Profiles[0];
        Assert.Equal(oldProfile.Id, profile.Id);
        Assert.Equal(oldProfile.PasswordId, profile.PasswordId);
        Assert.Equal(oldProfile.Username, profile.Username);
        Assert.True(profile.AutoLogin);
        var login = new AutoLoginSequence(profile);
        Assert.Equal(LoginStep.Username, login.Next("Name: "));
        Assert.Equal(LoginStep.Password, login.Next("(P)assword: "));
        Assert.Equal(customProfile, loaded.Settings.Profiles[1]);
        store.Save(loaded.Settings);
        Assert.Equal(loaded.Settings.Profiles, store.Load().Settings.Profiles);
    }

    /// <summary>
    /// The operating system decides whether a refused save is access denied or an IO error: Windows says
    /// access denied for a directory in the way, Unix for a folder without write permission. Callers roll back
    /// on IOException, so the store reports both the same way.
    /// </summary>
    [Fact]
    public void RefusedSaveIsReportedAsAnIOExceptionOnEveryPlatform()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new() { Theme = "Forest" });
        if (OperatingSystem.IsWindows() || !TryDenyWrites(_directory))
        {
            // Windows, or a Unix user (root) that permissions cannot stop: a directory blocks the atomic move.
            File.Delete(FilePath);
            Directory.CreateDirectory(FilePath);
        }
        try
        {
            var error = Assert.ThrowsAny<IOException>(() => store.Save(new() { Theme = "Paper" }));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
        finally
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Assert.Empty(Directory.EnumerateFiles(_directory, ".settings-*.tmp"));
    }

    private static bool TryDenyWrites(string directory)
    {
        if (OperatingSystem.IsWindows()) return false;
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try { File.WriteAllText(Path.Combine(directory, "probe"), ""); }
        catch (UnauthorizedAccessException) { return true; }
        File.Delete(Path.Combine(directory, "probe"));
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return false;
    }

    public void Dispose() => TestFiles.DeleteDirectory(_directory);
}
