using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Users;

internal sealed class GetRelationshipsEvent(IUserSocialShowcaseService social) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        social.ShowRelationships(session, packet.ReadInt());
}
