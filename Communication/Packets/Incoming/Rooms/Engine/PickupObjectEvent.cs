using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class PickupObjectEvent(IRoomItemPickupService pickup) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); // Furniture category; pickup resolves the item by its ID.
        var itemId = packet.ReadUInt();

        return pickup.PickUp(session, itemId);
    }
}
