using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Campaigns;

namespace Plus.Communication.Packets.Outgoing.Campaign
{
    public sealed class CampaignCalendarDataComposer(CalendarDataSnapshot data) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.CampaignCalendarDataComposer;
        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteString(data.Name);
            packet.WriteString(data.Image);
            packet.WriteInteger(data.CurrentDay);
            packet.WriteInteger(data.Days);
            packet.WriteInteger(data.Opened.Length);

            foreach (var day in data.Opened) {
                packet.WriteInteger(day);
            }

            packet.WriteInteger(data.Missed.Length);

            foreach (var day in data.Missed) {
                packet.WriteInteger(day);
            }
        }
    }
}
