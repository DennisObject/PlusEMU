using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class CreditFurniRedeemEvent(IItemRedemptionService redemptions) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        redemptions.RedeemCredits(room, session, packet.ReadUInt());

        return Task.CompletedTask;
    }
}
