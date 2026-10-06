using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public class RoomMuteSettingsComposer : IServerPacket
{
    private readonly bool _status;

    public uint MessageId => ServerPacketHeader.RoomMuteSettingsComposer;

    public RoomMuteSettingsComposer(bool status)
    {
        _status = status;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_status);
    }
}
