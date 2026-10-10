using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Chests;

namespace Plus.HabboHotel.Items.Interactor;

public sealed class InteractorWiredChest : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        var room = item.GetRoom();

        if (room == null) {
            return;
        }

        room.GetWired().WithChests(module =>
        {
            module.Read(item);
            module.Store.SaveSettings(item, (int)item.OwnerId, false, false, old => old with { Locked = true });
            module.Refresh(item);
        });
    }
    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (session != null && item.GetRoom() is { } room) {
            room.GetWired().WithChests(module => module.Open(session, item));
        }
    }
}
