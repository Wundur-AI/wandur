using Microsoft.Data.Sqlite;
using Wandur.Core.Mapping;
using Wandur.Core.Storage;

namespace Wandur.Core.Tests;

/// <summary>Rooms seen without a known route are placed near the map and dock once a move connects them.</summary>
public sealed class MapDockingTests : IDisposable
{
    private const string Area = "Coruscant";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wandur-map-docking-" + Guid.NewGuid());

    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private static RoomObservation At(string id, Dictionary<string, string?>? exits = null) =>
        new(id, "Room " + id, "", exits ?? [], Area, RoomDataSource.Gmcp);

    private static MapRoom Get(RoomMapTracker tracker, string id) => tracker.Snapshot.Rooms.Single(r => r.Id == id);

    private static void AssertAt(MapRoom room, double x, double y, double z = 0)
    {
        Assert.Equal(x, room.X, 6);
        Assert.Equal(y, room.Y, 6);
        Assert.Equal(z, room.Z, 6);
    }

    /// <summary>1 (0,0) east 2 (1,0) east 3 (2,0) north 4 (2,1) north 5 (2,2).</summary>
    private static RoomMapTracker Cluster()
    {
        var tracker = new RoomMapTracker();
        tracker.Observe(At("1"));
        tracker.Observe(At("2"), "east");
        tracker.Observe(At("3"), "east");
        tracker.Observe(At("4"), "north");
        tracker.Observe(At("5"), "north");
        return tracker;
    }

    [Fact]
    public void AFloatingRoomDocksWhenWalkedIntoThroughANortheastDoor()
    {
        var tracker = Cluster();
        tracker.Observe(At("9")); // recall, teleport or a server-driven move: no known route
        var parked = Get(tracker, "s:9");
        Assert.True(parked.X - 2 > 1); // beside the map, not on top of it
        tracker.Observe(At("1")); // back in the cluster
        tracker.Observe(At("9"), "northeast");
        AssertAt(Get(tracker, "s:9"), 1, 1);
        AssertAt(Get(tracker, "s:1"), 0, 0);
        Assert.False(tracker.CanUndo); // docking is observation, not a manual edit
    }

    [Fact]
    public void AFloatingRoomDocksWhenItsServerExitsNameAClusterRoom()
    {
        var tracker = Cluster();
        tracker.Observe(At("9", new() { ["sw"] = "1" }));
        AssertAt(Get(tracker, "s:9"), 1, 1);
        AssertAt(Get(tracker, "s:1"), 0, 0);
    }

    [Fact]
    public void AFloatingClusterDocksAsAUnitAndKeepsItsShape()
    {
        var tracker = Cluster();
        tracker.Observe(At("9"));
        tracker.Observe(At("10"), "west");
        tracker.Observe(At("11"), "north");
        tracker.Observe(At("1"));
        var before = tracker.Snapshot.Rooms.Where(r => r.Id is not ("s:9" or "s:10" or "s:11"))
            .ToDictionary(r => r.Id, r => (r.X, r.Y, r.Z));
        tracker.Observe(At("9"), "northwest");
        AssertAt(Get(tracker, "s:9"), -1, 1);
        AssertAt(Get(tracker, "s:10"), -2, 1);
        AssertAt(Get(tracker, "s:11"), -2, 2);
        Assert.Equal(before, tracker.Snapshot.Rooms.Where(r => before.ContainsKey(r.Id))
            .ToDictionary(r => r.Id, r => (r.X, r.Y, r.Z)));
    }

    [Fact]
    public void ADockedRoomLandingOnAnOccupiedSpotIsNudged()
    {
        var tracker = Cluster();
        tracker.Observe(At("9"));
        tracker.Observe(At("2"));
        tracker.Observe(At("9"), "northeast"); // (2,1) already holds room 4
        var docked = Get(tracker, "s:9");
        Assert.InRange(docked.X, 2.3, 2.4);
        Assert.Equal(1, docked.Y, 6);
        AssertAt(Get(tracker, "s:4"), 2, 1);
    }

    [Fact]
    public void AlreadyConnectedRoomsKeepTheirNonGridGeometry()
    {
        var tracker = new RoomMapTracker();
        tracker.Observe(At("1"));
        tracker.Observe(At("2"), "east");
        tracker.Observe(At("3"), "east");
        tracker.Observe(At("4"), "east");
        tracker.Observe(At("5"), "east");
        var before = tracker.Snapshot.Rooms.ToDictionary(r => r.Id, r => (r.X, r.Y, r.Z));
        tracker.Observe(At("1"), "west"); // a long winding road back to the start
        Assert.Contains(tracker.Snapshot.Links, l => l.FromId == "s:5" && l.ToId == "s:1" && l.Direction == "west");
        Assert.Equal(before, tracker.Snapshot.Rooms.ToDictionary(r => r.Id, r => (r.X, r.Y, r.Z)));
    }

    [Fact]
    public void AManuallyEditedFloatingRoomStaysAndTheUnanchoredClusterMovesInstead()
    {
        var tracker = Cluster();
        tracker.Observe(At("9"));
        var floating = Get(tracker, "s:9");
        Assert.True(tracker.UpsertRoom(floating with { X = 40, Y = 40 }));
        tracker.Observe(At("1"));
        tracker.Observe(At("9"), "northeast");
        AssertAt(Get(tracker, "s:9"), 40, 40);
        AssertAt(Get(tracker, "s:1"), 39, 39);
        AssertAt(Get(tracker, "s:2"), 40, 39);
        AssertAt(Get(tracker, "s:5"), 41, 41);
        // Only the manual edit is undoable; the docking itself is not.
        Assert.True(tracker.Undo());
        Assert.False(tracker.CanUndo);
    }

    [Fact]
    public void NothingMovesWhenBothSidesHoldManualEditsOrLocks()
    {
        var tracker = Cluster();
        tracker.Observe(At("9"));
        Assert.True(tracker.UpsertRoom(Get(tracker, "s:9") with { X = 40, Y = 40 }));
        Assert.True(tracker.UpsertRoom(Get(tracker, "s:3") with { IsLocked = true }));
        var before = tracker.Snapshot.Rooms.ToDictionary(r => r.Id, r => (r.X, r.Y, r.Z));
        tracker.Observe(At("1"));
        tracker.Observe(At("9"), "northeast");
        Assert.Equal(before, tracker.Snapshot.Rooms.ToDictionary(r => r.Id, r => (r.X, r.Y, r.Z)));
        Assert.Contains(tracker.Snapshot.Links, l => l.FromId == "s:1" && l.ToId == "s:9");
    }

    [Fact]
    public void RoomsPlacedFromServerCoordinatesAreNeverMoved()
    {
        var tracker = new RoomMapTracker();
        tracker.Observe(At("1") with { X = 0, Y = 0, Z = 0 });
        tracker.Observe(At("9") with { X = 50, Y = 0, Z = 0 });
        tracker.Observe(At("1") with { X = 0, Y = 0, Z = 0 });
        tracker.Observe(At("9") with { X = 50, Y = 0, Z = 0 }, "northeast");
        AssertAt(Get(tracker, "s:9"), 50, 0);
        AssertAt(Get(tracker, "s:1"), 0, 0);
    }

    [Fact]
    public void ASavedFarParkedRoomRepairsWhenTheLinkIsWalkedAndTheSaveKeepsIt()
    {
        MapRoom Saved(string id, double x, double y) => new("s:" + id, "Room " + id, "", Area, x, y, 0, false, id) { Revision = 5 };
        var saved = new MapSnapshot([Saved("1", 0, 0), Saved("2", 1, 0), Saved("9", 200, 0), Saved("10", 201, 0)],
            [new("s:1", "s:2", "east", true) { Revision = 5 }, new("s:2", "s:9", "northeast", true) { Revision = 5 },
             new("s:9", "s:10", "east", true) { Revision = 5 }],
            [], null, MapTrackingState.Unknown, RoomDataSource.Gmcp, 0);
        var store = new SqliteRoomMapStore(new ClientDatabase(Path.Combine(_directory, "wandur.db")), Path.Combine(_directory, "maps"));
        store.Save("mud.example", 4000, saved);
        var tracker = new RoomMapTracker(store.Load("mud.example", 4000));
        tracker.Observe(At("2"));
        tracker.Observe(At("9"), "northeast");
        AssertAt(Get(tracker, "s:9"), 2, 1);
        AssertAt(Get(tracker, "s:10"), 3, 1);
        Assert.False(tracker.CanUndo);
        store.Save("mud.example", 4000, tracker.Snapshot);
        var reloaded = store.Load("mud.example", 4000)!;
        AssertAt(reloaded.Rooms.Single(r => r.Id == "s:9"), 2, 1);
        AssertAt(reloaded.Rooms.Single(r => r.Id == "s:10"), 3, 1);
    }

    [Fact]
    public void ASavedFarParkedTextRoomRepairsWhenTheExistingLinkIsWalked()
    {
        RoomObservation Text(string name) => new(null, name, name + " prose for recognition", new Dictionary<string, string?>(), null, RoomDataSource.Text);
        var saved = new MapSnapshot([
                new MapRoom("t:hall", "Hall", "Hall prose for recognition", null, 0, 0, 0, true),
                new MapRoom("t:garden", "Garden", "Garden prose for recognition", null, 200, 0, 0, true)],
            [new("t:hall", "t:garden", "northeast", false)], [], null, MapTrackingState.Unknown, RoomDataSource.Text, 0);
        var tracker = new RoomMapTracker(saved);
        tracker.Observe(Text("Hall"));
        Assert.Equal("t:hall", tracker.Snapshot.CurrentRoomId);
        tracker.Observe(Text("Garden"), "northeast");
        Assert.Equal("t:garden", tracker.Snapshot.CurrentRoomId);
        AssertAt(Get(tracker, "t:garden"), 1, 1);
        Assert.True(Get(tracker, "t:garden").Revision > 0);
    }

    [Fact]
    public void ARoomWithNoKnownRouteIsPlacedBesideTheMapNotByRoomCount()
    {
        var tracker = new RoomMapTracker();
        tracker.Observe(At("0"));
        for (var i = 1; i < 100; i++)
            tracker.Observe(At(i.ToString()), i % 10 == 0 ? "north" : (i / 10) % 2 == 0 ? "east" : "west");
        var rooms = tracker.Snapshot.Rooms;
        var maxX = rooms.Max(r => r.X);
        var current = Get(tracker, tracker.Snapshot.CurrentRoomId!);
        tracker.Observe(At("500"));
        var placed = Get(tracker, "s:500");
        Assert.InRange(placed.X, maxX + 1, maxX + 3);
        Assert.Equal(current.Y, placed.Y, 6);
        Assert.Equal(current.Z, placed.Z, 6);
        Assert.DoesNotContain(tracker.Snapshot.Rooms, r => r.Id != placed.Id && r.X == placed.X && r.Y == placed.Y && r.Z == placed.Z);
    }

    [Fact]
    public void TheFirstRoomOfAnEmptyMapStartsAtTheOrigin()
    {
        var tracker = new RoomMapTracker();
        tracker.Observe(At("1"));
        AssertAt(Get(tracker, "s:1"), 0, 0);
    }
}
