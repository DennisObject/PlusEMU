using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public class UnbanUserFromRoomComposer : IServerPacket
{
    private readonly int _roomId;
    private readonly int _userId;

    public uint MessageId => ServerPacketHeader.UnbanUserFromRoomComposer;

    public UnbanUserFromRoomComposer(int roomId, int userId)
    {
        _roomId = roomId;
        _userId = userId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_roomId);
        packet.WriteInteger(_userId);
    }
}
