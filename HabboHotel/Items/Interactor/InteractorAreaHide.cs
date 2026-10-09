using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.AreaHide;

namespace Plus.HabboHotel.Items.Interactor;

public sealed class InteractorAreaHide : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        if (item.GetRoom() is { } room) {
            AreaHideState.Announce(room, item);
        }
    }
    public void OnRemove(GameClient? session, Item item)
    {
        if (item.GetRoom() is { } room) {
            AreaHideState.Announce(room, item, removed: true);
        }
    }
    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (session != null && hasRights && item.GetRoom() is { } room && room.CheckRights(session)) {
            AreaHideState.Toggle(room, item);
        }
    }
    public void OnWiredTrigger(Item item)
    {
        if (item.GetRoom() is { } room) {
            AreaHideState.Toggle(room, item);
        }
    }
}
