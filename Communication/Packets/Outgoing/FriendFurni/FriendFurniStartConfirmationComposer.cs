using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendFurni;

public class FriendFurniStartConfirmationComposer : IServerPacket
{
    private readonly uint _itemId;

    public uint MessageId => ServerPacketHeader.FriendFurniStartConfirmationComposer;

    public FriendFurniStartConfirmationComposer(uint itemId)
    {
        _itemId = itemId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_itemId);
        packet.WriteBoolean(true);
    }
}
