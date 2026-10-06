using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Campaign;

internal class OpenCampaignCalendarDoorEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => throw new NotImplementedException();
}
