using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Settings;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredRoomSettingsDataComposer(uint roomId, WiredRoomSettings settings, GameClient session) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredRoomSettingsDataComposer;
    public void Compose(IOutgoingPacket packet)
    {
        var view = settings.View(session);
        var saved = view.Settings;
        packet.WriteUInteger(roomId);
        packet.WriteInteger(saved.InspectMask);
        packet.WriteInteger(saved.ModifyMask);
        packet.WriteBoolean(view.CanInspect);
        packet.WriteBoolean(view.CanModify);
        packet.WriteBoolean(view.CanManage);
        packet.WriteString(saved.TimeZoneId);
    }
}
