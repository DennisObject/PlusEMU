using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.Communication.Packets.Outgoing.Inventory.Furni;

public sealed class FurniListAddComposer(InventoryItemSnapshot item) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.FurniListAddComposer;

    public void Compose(IOutgoingPacket packet) => InventoryFurnitureSerializer.Write(packet, item, added: true);
}
