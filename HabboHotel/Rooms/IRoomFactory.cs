using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomFactory
{
    Room Create(RoomData data);
    void Dispose(uint roomId);
}

internal sealed class LegacyRoomFactory : IRoomFactory
{
    public Room Create(RoomData data)
    {
        var room = new Room(data);
        room.Initiate();
        return room;
    }

    public void Dispose(uint roomId) { }
}
