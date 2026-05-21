using System.Collections.Concurrent;

namespace CutPig.Services;

public class RoomPresenceTracker
{
    private record ConnectionInfo(Guid UserId, Guid RoomId);

    private readonly ConcurrentDictionary<string, ConnectionInfo> _byConnection = new();
    private readonly ConcurrentDictionary<Guid, HashSet<Guid>> _onlineByRoom = new();
    private readonly object _gate = new();

    public void Add(string connectionId, Guid userId, Guid roomId)
    {
        _byConnection[connectionId] = new ConnectionInfo(userId, roomId);
        lock (_gate)
        {
            if (!_onlineByRoom.TryGetValue(roomId, out var set))
            {
                set = new HashSet<Guid>();
                _onlineByRoom[roomId] = set;
            }
            set.Add(userId);
        }
    }

    public (Guid UserId, Guid RoomId)? Remove(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var info)) return null;

        // Check if user still has any other connection in same room
        var stillOnline = _byConnection.Values.Any(c => c.UserId == info.UserId && c.RoomId == info.RoomId);
        if (!stillOnline)
        {
            lock (_gate)
            {
                if (_onlineByRoom.TryGetValue(info.RoomId, out var set))
                {
                    set.Remove(info.UserId);
                    if (set.Count == 0) _onlineByRoom.TryRemove(info.RoomId, out _);
                }
            }
        }
        return (info.UserId, info.RoomId);
    }

    public IReadOnlySet<Guid> OnlineInRoom(Guid roomId)
    {
        lock (_gate)
        {
            if (_onlineByRoom.TryGetValue(roomId, out var set))
                return new HashSet<Guid>(set);
            return new HashSet<Guid>();
        }
    }

    public bool IsOnline(Guid roomId, Guid userId)
    {
        lock (_gate)
        {
            return _onlineByRoom.TryGetValue(roomId, out var set) && set.Contains(userId);
        }
    }
}
