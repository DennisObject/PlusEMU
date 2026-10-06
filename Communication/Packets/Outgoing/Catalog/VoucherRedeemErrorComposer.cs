using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class VoucherRedeemErrorComposer : IServerPacket
{
    private readonly VoucherRedeemError _type;
    public uint MessageId => ServerPacketHeader.VoucherRedeemErrorComposer;

    public VoucherRedeemErrorComposer(VoucherRedeemError type) => _type = type;

    public void Compose(IOutgoingPacket packet) => packet.WriteString(((int)_type).ToString(System.Globalization.CultureInfo.InvariantCulture));
}
