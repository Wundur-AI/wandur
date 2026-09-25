using System.Text.Json;
using Wandur.Core.Settings;
using Wandur.Core.Storage;

namespace Wandur.Core.Tests;

public sealed class WindowSkinSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "wandur-skin-" + Guid.NewGuid());

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Skin\":null}")]
    [InlineData("{\"Skin\":\"Unknown\"}")]
    public void OldOrUnknownSkinRemainsLoadable(string json)
    {
        var settings = JsonSerializer.Deserialize<ClientSettings>(json)!;
        settings.Validate();
        Assert.Equal("Fleet", WindowSkinId.Normalize(settings.Skin));
    }

    [Theory]
    [InlineData("Fleet")]
    [InlineData("Armored")]
    [InlineData("System")]
    [InlineData("FutureSkin")]
    public void SkinSurvivesBothStoresWithoutLosingPaletteOrProfiles(string skin)
    {
        var settings = JsonSerializer.Deserialize<ClientSettings>(
            $$"""{"Skin":"{{skin}}","Theme":"Slate","FontSize":17,"Profiles":[{"Name":"Kept","Host":"localhost"}]}""")!;
        var json = new SettingsStore(Path.Combine(_path, "settings.json"));
        json.Save(settings);
        AssertSaved(json.Load().Settings, skin);
        var database = new ClientDatabase(Path.Combine(_path, "wandur.db"));
        var sqlite = new SqliteSettingsStore(database, Path.Combine(_path, "unused.json"));
        sqlite.Save(settings);
        var reopened = new SqliteSettingsStore(database, Path.Combine(_path, "unused.json")).Load();
        Assert.Null(reopened.Warning);
        AssertSaved(reopened.Settings, skin);
    }

    private static void AssertSaved(ClientSettings settings, string skin)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        Assert.True(doc.RootElement.TryGetProperty("Skin", out var value), "Skin choice must be persisted.");
        Assert.Equal(skin, value.GetString());
        Assert.Equal("Slate", settings.Theme);
        Assert.Equal(17, settings.FontSize);
        Assert.Equal("Kept", Assert.Single(settings.Profiles).Name);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }
}
