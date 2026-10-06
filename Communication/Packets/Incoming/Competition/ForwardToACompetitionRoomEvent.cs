using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Competition;

internal class ForwardToACompetitionRoomEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => throw new NotImplementedException();
}
