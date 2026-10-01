using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ItemUpdateComposer : IServerPacket
{
    private readonly Item _item;
    public uint MessageId => ServerPacketHeader.ItemUpdateComposer;

    public ItemUpdateComposer(Item item)
    {
        _item = item;
    }

    public void Compose(IOutgoingPacket packet)
    {
        WriteWallItem(packet, _item);
    }

    private void WriteWallItem(IOutgoingPacket packet, Item item)
    {
        packet.WriteString(item.Id.ToString());
        packet.WriteInteger(item.Definition.SpriteId);
        packet.WriteString(item.WallCoordinates);
        switch (item.Definition.InteractionType)
        {
            case InteractionType.Postit:
                packet.WriteString(item.LegacyDataString.Split(' ')[0]);
                break;
            default:
                packet.WriteString(item.LegacyDataString);
                break;
        }
        packet.WriteInteger(-1);
        packet.WriteInteger(item.Definition.Modes > 1 ? 1 : 0);
        packet.WriteInteger(item.UserId);
    }
}
