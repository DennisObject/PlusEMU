using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Scoped]
public interface IRoomComponent
{
    void Initiate(Room room);
    void Initiated();
}
