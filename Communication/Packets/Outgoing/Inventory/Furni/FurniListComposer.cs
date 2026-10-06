using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.Communication.Packets.Outgoing.Inventory.Furni;

public sealed class FurniListComposer(ImmutableArray<InventoryItemSnapshot> items, int pages, int page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.FurniListComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(pages);
        packet.WriteInteger(page);
        packet.WriteInteger(items.Length);

        foreach (var item in items)
        {
            InventoryFurnitureSerializer.Write(packet, item, added: false);
        }
    }
}
