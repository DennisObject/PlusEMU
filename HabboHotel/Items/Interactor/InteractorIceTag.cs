using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Interactor;

public sealed class InteractorIceTag : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        if (item.Definition.InteractionType == InteractionType.IceTagPole) {
            item.LegacyDataString = "0";
        }
    }
    public void OnRemove(GameClient? session, Item item) => item.GetRoom()?.GetIceTag().Removed(item);
}
