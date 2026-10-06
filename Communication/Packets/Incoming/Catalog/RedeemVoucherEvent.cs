using Plus.HabboHotel.Catalog.Vouchers;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public sealed class RedeemVoucherEvent(IVoucherRedemptionService vouchers) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        vouchers.Redeem(session, packet.ReadString());
        return Task.CompletedTask;
    }
}
