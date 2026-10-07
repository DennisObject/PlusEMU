using Plus.HabboHotel.Campaigns;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Campaign;

public sealed class OpenCampaignCalendarDoorEvent(ICampaignCalendarService calendars) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        calendars.Open(session, packet.ReadString(), packet.ReadInt());

        return Task.CompletedTask;
    }
}
