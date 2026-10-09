namespace Wandur.Core.Mapping;

public sealed partial class RoomMapTracker
{
    // Set once a world reports room coordinates; its layout is then the server's, never ours to shift.
    private bool _serverCoordinates;

    private static (double X, double Y, double Z) Offset(string? direction) => direction switch
    {
        "north" => (0, 1, 0), "south" => (0, -1, 0), "east" => (1, 0, 0), "west" => (-1, 0, 0),
        "northeast" => (1, 1, 0), "northwest" => (-1, 1, 0), "southeast" => (1, -1, 0), "southwest" => (-1, -1, 0),
        "up" => (0, 0, 1), "down" => (0, 0, -1), _ => (1, 0, 0)
    };

    private (double X, double Y, double Z) Position(string? origin, string? direction, string? area = null)
    {
        (double X, double Y, double Z) position;
        if (origin is not null && _rooms.TryGetValue(origin, out var room))
        {
            var offset = Offset(direction);
            position = (room.X + offset.X, room.Y + offset.Y, room.Z + offset.Z);
        }
        else position = Unplaced(area);
        return Free(position, null);
    }

    /// <summary>A room with no known route goes just beside the map, near the current area, until a move connects it.</summary>
    private (double X, double Y, double Z) Unplaced(string? area)
    {
        if (_rooms.Count == 0) return (0, 0, 0);
        var key = area ?? "";
        var near = _rooms.Values.Where(r => (r.Area ?? "") == key).ToArray();
        if (near.Length == 0) near = _rooms.Values.ToArray();
        var reference = _current is not null && _rooms.TryGetValue(_current, out var current) && near.Contains(current)
            ? current : near.MaxBy(r => r.X)!;
        return (Math.Floor(near.Max(r => r.X)) + 2, reference.Y, reference.Z);
    }

    // Display overlap is not proof that two rooms are identical.
    private (double X, double Y, double Z) Free((double X, double Y, double Z) position, string? self)
    {
        while (_rooms.Values.Any(r => r.Id != self && r.X == position.X && r.Y == position.Y && r.Z == position.Z)) position.X += 0.35;
        return position;
    }

    /// <summary>
    /// Evidence that <paramref name="fromId"/> leads <paramref name="direction"/> to <paramref name="toId"/> pulls a
    /// misplaced part of the map into place. A part is the rooms joined to one end by links that already fit the grid.
    /// It moves only when the other end is not inside it, when every compass link leaving it fits once it has moved
    /// (several long links from one parked island all fit after the same shift; links that would still not fit are
    /// real, non-grid geometry and stay as drawn), and when it holds no manual edit, lock, drawn exit line or server
    /// coordinates. If both ends could move, the smaller part does. Not an undoable edit.
    /// </summary>
    private void Dock(string fromId, string toId, string direction)
    {
        if (_serverCoordinates || fromId == toId || !_rooms.TryGetValue(fromId, out var from) ||
            !_rooms.TryGetValue(toId, out var to) ||
            !_links.Any(l => l.FromId == fromId && l.Direction == direction && l.ToId == toId)) return;
        var offset = Offset(direction);
        var dx = from.X + offset.X - to.X;
        var dy = from.Y + offset.Y - to.Y;
        var dz = from.Z + offset.Z - to.Z;
        if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && Math.Abs(dz) < 0.5) return;
        var toSide = Movable(toId, fromId, dx, dy, dz);
        var fromSide = Movable(fromId, toId, -dx, -dy, -dz);
        if (toSide is not null && (fromSide is null || toSide.Count <= fromSide.Count)) Translate(toSide, toId, dx, dy, dz);
        else if (fromSide is not null) Translate(fromSide, fromId, -dx, -dy, -dz);
    }

    /// <summary>The part around <paramref name="start"/> that can shift by (dx, dy, dz) to meet <paramref name="other"/>,
    /// or null when it contains <paramref name="other"/>, is anchored, or would leave a compass link that still does not fit.</summary>
    private HashSet<string>? Movable(string start, string other, double dx, double dy, double dz)
    {
        var part = new HashSet<string> { start };
        var queue = new Queue<string>([start]);
        while (queue.TryDequeue(out var id))
            foreach (var link in _links)
            {
                var next = link.FromId == id ? link.ToId : link.ToId == id ? link.FromId : null;
                if (next is null || !_rooms.ContainsKey(next) || !Compass(link.Direction) || !Fits(link, 0, 0, 0, null)) continue;
                if (next == other) return null;
                if (part.Add(next)) queue.Enqueue(next);
            }
        if (Anchored(part)) return null;
        foreach (var link in _links)
        {
            if (!Compass(link.Direction) || !_rooms.ContainsKey(link.FromId) || !_rooms.ContainsKey(link.ToId) ||
                part.Contains(link.FromId) == part.Contains(link.ToId)) continue;
            if (!Fits(link, dx, dy, dz, part)) return null;
        }
        return part;
    }

    private static bool Compass(string direction) => direction is "north" or "south" or "east" or "west" or
        "northeast" or "northwest" or "southeast" or "southwest" or "up" or "down";

    /// <summary>Whether a link matches its direction on the grid, with the rooms in <paramref name="moved"/> shifted.</summary>
    private bool Fits(MapLink link, double dx, double dy, double dz, HashSet<string>? moved)
    {
        var a = _rooms[link.FromId];
        var b = _rooms[link.ToId];
        var (ax, ay, az) = moved?.Contains(a.Id) == true ? (a.X + dx, a.Y + dy, a.Z + dz) : (a.X, a.Y, a.Z);
        var (bx, by, bz) = moved?.Contains(b.Id) == true ? (b.X + dx, b.Y + dy, b.Z + dz) : (b.X, b.Y, b.Z);
        var offset = Offset(link.Direction);
        return Math.Abs(ax + offset.X - bx) <= 1 && Math.Abs(ay + offset.Y - by) <= 1 && Math.Abs(az + offset.Z - bz) < 0.5;
    }

    private bool Anchored(HashSet<string> rooms) =>
        rooms.Any(id => _rooms[id] is { IsManuallyEdited: true } or { IsLocked: true }) ||
        _links.Any(l => l.LinePoints.Count > 0 && (rooms.Contains(l.FromId) || rooms.Contains(l.ToId)));

    private void Translate(HashSet<string> rooms, string anchor, double dx, double dy, double dz)
    {
        foreach (var id in rooms)
        {
            var room = _rooms[id];
            _rooms[id] = room with { X = room.X + dx, Y = room.Y + dy, Z = room.Z + dz };
        }
        // The joining room first, so it takes the exact spot when that is free.
        foreach (var id in rooms.OrderByDescending(id => id == anchor))
        {
            var room = _rooms[id];
            var (x, y, z) = Free((room.X, room.Y, room.Z), id);
            // A new revision so stores keep the new position over the saved one.
            _rooms[id] = room with { X = x, Y = y, Z = z, Revision = NextRevision() };
        }
    }
}
