using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class JoinGroupEvent(IGroupParticipationService participation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => participation.Join(session, packet.ReadInt());
}
