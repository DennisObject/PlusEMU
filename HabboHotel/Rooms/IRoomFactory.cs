using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomFactory
{
    Room Create(RoomData data);
    void Dispose(uint roomId);
}
