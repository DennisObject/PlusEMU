using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Users;

internal sealed class SetRelationshipEvent(IMessengerSocialMutationService social) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (packet.Buffer.Length < 8) {
            return Task.CompletedTask;
        }

        return social.SetRelationship(session, packet.ReadInt(), packet.ReadInt());
    }
}
