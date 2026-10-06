using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Notifications;

public class RoomErrorNotifComposer : IServerPacket
{
    private readonly PetPlacementError _error;
    public uint MessageId => ServerPacketHeader.RoomErrorNotifComposer;

    public RoomErrorNotifComposer(PetPlacementError error)
    {
        _error = error;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger((int)_error);
}