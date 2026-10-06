using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.Communication.Packets.Outgoing.Inventory.Furni;

internal static class InventoryFurnitureSerializer
{
    public static void Write(IOutgoingPacket packet, InventoryItemSnapshot item, bool added)
    {
        packet.WriteUInteger(item.Id);
        packet.WriteString(item.Type);
        packet.WriteUInteger(item.Id);
        packet.WriteInteger(item.SpriteId);
        packet.WriteInteger((int)item.Category);
        FurnitureDataSerializer.Write(packet, item.Data, item.UniqueNumber, item.UniqueSeries);
        packet.WriteBoolean(item.Recyclable);
        packet.WriteBoolean(item.Tradeable);
        packet.WriteBoolean(added ? item.StackableWhenAdded : item.Stackable);
        packet.WriteBoolean(item.MarketplaceSellable);
        packet.WriteInteger(-1);
        packet.WriteBoolean(false);
        packet.WriteInteger(-1);

        if (!item.IsWallItem) {
            packet.WriteString(string.Empty);
            packet.WriteInteger(0);
        }
    }
}
