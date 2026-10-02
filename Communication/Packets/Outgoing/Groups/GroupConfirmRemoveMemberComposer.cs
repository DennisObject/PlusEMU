using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupConfirmRemoveMemberComposer : IServerPacket
{
    private readonly int _userId;
    private readonly int _furnitureCount;

    public uint MessageId => ServerPacketHeader.GroupConfirmRemoveMemberComposer;

    public GroupConfirmRemoveMemberComposer(int userId, int furnitureCount)
    {
        _userId = userId;
        _furnitureCount = furnitureCount;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_userId);
        packet.WriteInteger(_furnitureCount);
    }
}
