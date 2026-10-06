using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Scoped]
public interface IRoomComponent
{
    int Order => 0;
    void Initiate(Room room);
    void Initiated();
}
