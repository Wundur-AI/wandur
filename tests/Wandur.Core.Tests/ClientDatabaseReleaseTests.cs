using Wandur.Core.History;
using Wandur.Core.Settings;
using Wandur.Core.Storage;

namespace Wandur.Core.Tests;

/// <summary>
/// Pooled SQLite connections keep the database file open after each connection is disposed. On Windows that
/// blocks deleting or replacing the file; these pin that disposing the database is what lets it go.
/// </summary>
public sealed class ClientDatabaseReleaseTests
{
    [Fact]
    public void DisposingTheDatabaseReleasesItsFileSoItCanBeDeleted()
    {
        var directory = TestFiles.CreateDirectory("wandur-release-");
        try
        {
            var path = Path.Combine(directory, "wandur.db");
            var database = new ClientDatabase(path);
            new SqliteSettingsStore(database, Path.Combine(directory, "settings.json")).Save(new ClientSettings());
            new SqliteHistoryStore(database).Append(new HistorySession("s", "host:4000", "World", "Rowan", DateTimeOffset.UnixEpoch),
                [new HistoryEntry(0, DateTimeOffset.UnixEpoch, "received", "Hello.")]);
            // The check can see a held file. Disposing the connection returns it to the pool, still open.
            // (Other tests clear every pool at times, so the pooled state alone is not asserted.)
            using (database.OpenConnection()) Assert.True(TestFiles.IsOpenByThisProcess(path));

            database.Dispose();

            Assert.False(TestFiles.IsOpenByThisProcess(path));
            File.Delete(path);
            Assert.False(File.Exists(path));
            // Closing the last connection checkpoints the write-ahead log, so nothing is left beside it.
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory, "wandur.db*"));
        }
        finally { TestFiles.DeleteDirectory(directory); }
    }

    [Fact]
    public void DisposingOneDatabaseLeavesAnotherDatabaseUsable()
    {
        var directory = TestFiles.CreateDirectory("wandur-release-");
        try
        {
            using var kept = new ClientDatabase(Path.Combine(directory, "kept.db"));
            var keptStore = new SqliteSettingsStore(kept, Path.Combine(directory, "kept.json"));
            keptStore.Save(new ClientSettings { Theme = "Paper" });
            using (var released = new ClientDatabase(Path.Combine(directory, "released.db")))
                new SqliteSettingsStore(released, Path.Combine(directory, "released.json")).Save(new ClientSettings());
            Assert.False(TestFiles.IsOpenByThisProcess(Path.Combine(directory, "released.db")));
            Assert.Equal("Paper", keptStore.Load().Settings.Theme);
        }
        finally { TestFiles.DeleteDirectory(directory); }
    }
}
