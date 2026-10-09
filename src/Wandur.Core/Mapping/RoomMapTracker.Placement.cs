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
    /// floating part of the map into place. Only when the two sides were not already connected some other way
    /// (that is real, non-grid geometry and stays as drawn) and only the side without manual edits, locks, drawn
    /// exit lines or server coordinates moves, the smaller one when both could. Not an undoable edit.
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
        var adjacency = Adjacency(fromId, toId);
        if (Component(adjacency, toId, fromId) is not { } toSide) return;
        var fromSide = Component(adjacency, fromId, toId)!;
        var toFree = !Anchored(toSide);
        var fromFree = !Anchored(fromSide);
        if (toFree && (!fromFree || toSide.Count <= fromSide.Count)) Translate(toSide, toId, dx, dy, dz);
        else if (fromFree) Translate(fromSide, fromId, -dx, -dy, -dz);
    }

    /// <summary>Undirected neighbours, ignoring every link between the two rooms being joined.</summary>
    private Dictionary<string, List<string>> Adjacency(string first, string second)
    {
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var link in _links)
        {
            if ((link.FromId == first && link.ToId == second) || (link.FromId == second && link.ToId == first) ||
                link.FromId == link.ToId || !_rooms.ContainsKey(link.FromId) || !_rooms.ContainsKey(link.ToId)) continue;
            Add(link.FromId, link.ToId); Add(link.ToId, link.FromId);
        }
        return adjacency;
        void Add(string a, string b)
        {
            if (!adjacency.TryGetValue(a, out var list)) adjacency[a] = list = [];
            list.Add(b);
        }
    }

    /// <summary>The rooms reachable from <paramref name="start"/>, or null when that includes <paramref name="other"/>.</summary>
    private static HashSet<string>? Component(Dictionary<string, List<string>> adjacency, string start, string other)
    {
        var seen = new HashSet<string> { start };
        var queue = new Queue<string>([start]);
        while (queue.TryDequeue(out var id))
            foreach (var next in adjacency.GetValueOrDefault(id) ?? [])
            {
                if (next == other) return null;
                if (seen.Add(next)) queue.Enqueue(next);
            }
        return seen;
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
