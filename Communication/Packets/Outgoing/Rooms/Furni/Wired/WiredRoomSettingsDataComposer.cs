using Plus.HabboHotel.Items.Wired.Settings;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredRoomSettingsDataComposer(WiredRoomSettingsView view) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredRoomSettingsDataComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(view.RoomId);
        packet.WriteInteger(view.InspectMask);
        packet.WriteInteger(view.ModifyMask);
        packet.WriteBoolean(view.CanInspect);
        packet.WriteBoolean(view.CanModify);
        packet.WriteBoolean(view.CanManage);
        packet.WriteString(view.TimeZoneId);
    }
}
