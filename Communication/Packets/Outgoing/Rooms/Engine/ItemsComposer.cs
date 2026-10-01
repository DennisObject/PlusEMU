using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class ItemsComposer : IServerPacket
{
    private readonly Item[] _objects;
    private readonly Room _room;

    public uint MessageId => ServerPacketHeader.ItemsComposer;

    public ItemsComposer(Item[] objects, Room room)
    {
        _objects = objects;
        _room = room;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.SerializeOwners(_objects.Select(GetOwnerId), _room);
        packet.WriteInteger(_objects.Length);
        foreach (var item in _objects) WriteWallItem(packet, item, GetOwnerId(item));
    }

    private int GetOwnerId(Item item) => item.UserId != 0 ? item.UserId : _room.OwnerId;

    private void WriteWallItem(IOutgoingPacket packet, Item item, int userId)
    {
        packet.WriteString(item.Id.ToString());
        packet.WriteInteger(item.Definition.SpriteId);
        try
        {
            packet.WriteString(item.WallCoordinates);
        }
        catch
        {
            packet.WriteString("");
        }
        ItemBehaviourUtility.GenerateWallExtradata(item, packet);
        packet.WriteInteger(-1);
        packet.WriteInteger(item.Definition.Modes > 1 ? 1 : 0);
        packet.WriteInteger(userId);
    }
}