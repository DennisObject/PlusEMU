using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendFurni;

public class FriendFurniOtherLockConfirmedComposer : IServerPacket
{
    private readonly uint _itemId;
    public uint MessageId => ServerPacketHeader.FriendFurniOtherLockConfirmedComposer;

    public FriendFurniOtherLockConfirmedComposer(uint itemId)
    {
        _itemId = itemId;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteUInteger(_itemId);
}