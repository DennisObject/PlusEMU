using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Interactor;

public interface IFurniInteractor
{
    void OnPlace(GameClient? session, Item item)
    {
    }
    void OnMove(GameClient? session, ThreeDCoord from, ThreeDCoord to)
    {
    }
    void OnRemove(GameClient? session, Item item)
    {
    }
    void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
    }
    void OnWiredTrigger(Item item)
    {
    }
    void OnWalkOn(RoomUser user)
    {
    }
    void OnWalkOff(RoomUser user)
    {
    }
}
