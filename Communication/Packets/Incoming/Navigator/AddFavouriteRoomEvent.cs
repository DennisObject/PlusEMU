using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Incoming.Navigator;

public class AddFavouriteRoomEvent(INavigatorFavoriteService favorites) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        favorites.Add(session, packet.ReadUInt());
        return Task.CompletedTask;
    }
}
