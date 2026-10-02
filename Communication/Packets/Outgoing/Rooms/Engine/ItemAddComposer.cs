using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ItemAddComposer : IServerPacket
{
    private readonly Item _item;
    public uint MessageId => ServerPacketHeader.ItemAddComposer;

    public ItemAddComposer(Item item)
    {
        _item = item;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_item.Id.ToString());
        packet.WriteInteger(_item.Definition.SpriteId);
        packet.WriteString(_item.WallCoordinates != null ? _item.WallCoordinates : string.Empty);
        ItemBehaviourUtility.GenerateWallExtradata(_item, packet);
        packet.WriteInteger(-1);
        packet.WriteInteger(_item.Definition.Modes > 1 ? 1 : 0); // Type New R63 ('use bottom')
        packet.WriteInteger(_item.UserId);
        RoomEngineSerializers.WriteFurnitureMetadata(packet, _item);
        packet.WriteString(_item.Username);
    }
}
