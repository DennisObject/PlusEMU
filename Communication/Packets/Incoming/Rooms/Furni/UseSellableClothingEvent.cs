using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class UseSellableClothingEvent(IItemRedemptionService redemptions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        redemptions.RedeemClothing(session, packet.ReadUInt());

        return Task.CompletedTask;
    }
}
