using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class RoomEntryInfoComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly bool _isOwner;

    public uint MessageId => ServerPacketHeader.RoomEntryInfoComposer;

    public RoomEntryInfoComposer(uint roomId, bool isOwner)
    {
        _roomId = roomId;
        _isOwner = isOwner;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
        packet.WriteBoolean(_isOwner);
    }
}
