using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendFurni;

public class FriendFurniCancelLockComposer : IServerPacket
{
    private readonly uint _itemId;
    public uint MessageId => ServerPacketHeader.FriendFurniCancelLockComposer;

    public FriendFurniCancelLockComposer(uint itemId)
    {
        _itemId = itemId;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteUInteger(_itemId);
}
