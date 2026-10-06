using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Incoming.Navigator;

public class RemoveFavouriteRoomEvent(INavigatorFavoriteService favorites) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        favorites.Remove(session, packet.ReadUInt());

        return Task.CompletedTask;
    }
}
