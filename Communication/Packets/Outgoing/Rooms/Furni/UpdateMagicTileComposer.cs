using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni;

public class UpdateMagicTileComposer : IServerPacket
{
    private readonly uint _itemId;
    private readonly int _height;

    public uint MessageId => ServerPacketHeader.UpdateMagicTileComposer;

    public UpdateMagicTileComposer(uint itemId, int height)
    {
        _itemId = itemId;
        _height = height;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_itemId);
        packet.WriteInteger(_height);
    }
}
