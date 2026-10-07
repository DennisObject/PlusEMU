using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Music;

namespace Plus.HabboHotel.Items.Interactor
{
    public sealed class InteractorRoomMusic : IFurniInteractor
    {
        public void OnPlace(GameClient? session, Item item) => item.GetRoom()?.Music.Attach(item);
        public void OnRemove(GameClient? session, Item item) => item.GetRoom()?.Music.Detach(item);
        public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
        {
            if (session != null) {
                item.GetRoom()?.Music.Use(session, item, request, hasRights);
            }
        }
    }
}
