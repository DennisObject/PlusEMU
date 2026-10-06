using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Logs;

public sealed class ChatlogEntry
{
    private readonly WeakReference<Habbo>? _playerReference;
    private readonly WeakReference<Room>? _roomReference;

    public ChatlogEntry(int playerId, uint roomId, string message, double timestamp, Habbo? player = null, Room? instance = null)
    {
        PlayerId = playerId;
        RoomId = roomId;
        Message = message;
        Timestamp = timestamp;
        if (player != null)
            _playerReference = new(player);
        if (instance != null)
            _roomReference = new(instance);
    }

    public int PlayerId { get; }

    public uint RoomId { get; }

    public string Message { get; }

    public double Timestamp { get; }

    public Habbo? PlayerNullable() => _playerReference != null && _playerReference.TryGetTarget(out var player) ? player : null;

    public Room? RoomNullable() => _roomReference != null && _roomReference.TryGetTarget(out var room) && !room.MDisposed ? room : null;
}