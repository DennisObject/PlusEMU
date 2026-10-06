using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class RemoveGroupFavouriteEvent(IGroupParticipationService participation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => participation.RemoveFavourite(session);
}
