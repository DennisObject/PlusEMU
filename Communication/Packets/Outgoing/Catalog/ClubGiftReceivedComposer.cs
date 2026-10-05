using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class ClubGiftReceivedComposer(ClubGiftReceivedSnapshot gift) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ClubGiftReceivedComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(gift.CatalogName);
        packet.WriteInteger(1);
        packet.WriteString(gift.ProductType);
        packet.WriteInteger(gift.SpriteId);
        packet.WriteString("");
        packet.WriteInteger(gift.Amount);
        packet.WriteBoolean(false);
    }
}
public class PickMonthlyClubGiftComposer(int available) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PickMonthlyClubGiftComposer;
    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(available);
}
