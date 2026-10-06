using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni;

public class GnomeBoxComposer : IServerPacket
{
    private readonly uint _itemId;
    public uint MessageId => ServerPacketHeader.GnomeBoxComposer;

    public GnomeBoxComposer(uint itemId)
    {
        _itemId = itemId;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteUInteger(_itemId);
}
