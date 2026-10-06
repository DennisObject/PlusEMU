using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public class RoomSettingsSavedComposer : IServerPacket
{
    private readonly uint _roomId;

    public uint MessageId => ServerPacketHeader.RoomSettingsSavedComposer;

    public RoomSettingsSavedComposer(uint roomId)
    {
        _roomId = roomId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
    }
}
