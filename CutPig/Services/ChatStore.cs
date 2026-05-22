using System.Collections.Concurrent;

namespace CutPig.Services;

public record ChatMessage(Guid Id, Guid RoomId, Guid UserId, string DisplayName, string Text, DateTime CreatedAt);

public class ChatStore
{
    private const int MaxPerRoom = 200;
    private readonly ConcurrentDictionary<Guid, LinkedList<ChatMessage>> _byRoom = new();
    private readonly object _gate = new();

    public ChatMessage Append(Guid roomId, Guid userId, string displayName, string text)
    {
        var msg = new ChatMessage(Guid.NewGuid(), roomId, userId, displayName, text, DateTime.UtcNow);
        lock (_gate)
        {
            var list = _byRoom.GetOrAdd(roomId, _ => new LinkedList<ChatMessage>());
            list.AddLast(msg);
            while (list.Count > MaxPerRoom) list.RemoveFirst();
        }
        return msg;
    }

    public IReadOnlyList<ChatMessage> Recent(Guid roomId, int limit = 50)
    {
        lock (_gate)
        {
            if (!_byRoom.TryGetValue(roomId, out var list)) return Array.Empty<ChatMessage>();
            return list.Reverse().Take(limit).Reverse().ToList();
        }
    }

    public void Clear(Guid roomId)
    {
        _byRoom.TryRemove(roomId, out _);
    }
}
