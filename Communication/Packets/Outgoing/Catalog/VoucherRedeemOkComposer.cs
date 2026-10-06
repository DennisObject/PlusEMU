using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;


// TODO @80O: Implement
public class VoucherRedeemOkComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.VoucherRedeemOkComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(""); //productName
        packet.WriteString(""); //productDescription
    }
}
