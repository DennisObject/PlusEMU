using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Chests;

namespace Plus.HabboHotel.Items.Interactor;

public sealed class InteractorWiredContract : IFurniInteractor
{
    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        if (session != null && item.GetRoom() is { } room) {
            room.GetWired().WithChests(module => WiredChestContractEditor.Open(module, session, item));
        }
    }
}
