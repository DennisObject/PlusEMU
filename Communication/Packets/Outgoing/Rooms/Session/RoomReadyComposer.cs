using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Session;

public class RoomReadyComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly string _model;

    public uint MessageId => ServerPacketHeader.RoomReadyComposer;

    public RoomReadyComposer(uint roomId, string model)
    {
        _roomId = roomId;
        _model = model;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_model);
        packet.WriteUInteger(_roomId);
    }
}
