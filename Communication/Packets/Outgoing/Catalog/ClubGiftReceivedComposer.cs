using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Catalog;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class ClubGiftReceivedComposer(CatalogItem item) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ClubGiftReceivedComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(item.CatalogName);
        packet.WriteInteger(1);
        packet.WriteString(item.Definition.ProductType);
        packet.WriteInteger(item.Definition.SpriteId);
        packet.WriteString("");
        packet.WriteInteger(item.Amount);
        packet.WriteBoolean(false);
    }
}
public class PickMonthlyClubGiftComposer(int available) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PickMonthlyClubGiftComposer;
    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(available);
}
