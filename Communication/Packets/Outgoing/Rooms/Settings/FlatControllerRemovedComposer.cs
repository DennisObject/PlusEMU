using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public class FlatControllerRemovedComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly int _userId;
    public uint MessageId => ServerPacketHeader.FlatControllerRemovedComposer;

    public FlatControllerRemovedComposer(uint roomId, int userId)
    {
        _roomId = roomId;
        _userId = userId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
        packet.WriteInteger(_userId);
    }
}
