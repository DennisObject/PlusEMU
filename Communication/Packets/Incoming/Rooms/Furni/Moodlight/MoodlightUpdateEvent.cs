using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Moodlight;

internal class MoodlightUpdateEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, true) || room.MoodlightData == null)
            return Task.CompletedTask;
        var item = room.GetRoomItemHandler().GetItem(room.MoodlightData.ItemId);
        if (item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.Moodlight)
            return Task.CompletedTask;
        var preset = packet.ReadInt();
        var backgroundMode = packet.ReadInt();
        var colorCode = packet.ReadString();
        var intensity = packet.ReadInt();
        var backgroundOnly = backgroundMode >= 2;
        room.MoodlightData.Enabled = true;
        room.MoodlightData.CurrentPreset = preset;
        room.MoodlightData.UpdatePreset(preset, colorCode, intensity, backgroundOnly);
        item.LegacyDataString = room.MoodlightData.GenerateExtraData();
        item.UpdateState();
        return Task.CompletedTask;
    }
}