using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Campaign;

public sealed class CampaignCalendarDoorOpenedComposer(bool opened, string product = "", string image = "", string furniture = "") : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CampaignCalendarDoorOpenedComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(opened);
        packet.WriteString(product);
        packet.WriteString(image);
        packet.WriteString(furniture);
    }
}
