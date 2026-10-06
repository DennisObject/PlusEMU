using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Campaign;

internal class OpenCampaignCalendarDoorAsStaffEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => throw new NotImplementedException();
}
