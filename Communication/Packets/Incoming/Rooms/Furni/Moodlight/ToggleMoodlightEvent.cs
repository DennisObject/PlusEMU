using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Moodlight;

internal class ToggleMoodlightEvent : RoomPacketEvent
{
    public override Task Parse(Room room,GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, true) || room.MoodlightData == null)
            return Task.CompletedTask;
        var item = room.GetRoomItemHandler().GetItem(room.MoodlightData.ItemId);
        if (item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.Moodlight)
            return Task.CompletedTask;
        if (room.MoodlightData.Enabled)
            room.MoodlightData.Disable();
        else
            room.MoodlightData.Enable();
        item.LegacyDataString = room.MoodlightData.GenerateExtraData();
        item.UpdateState();
        return Task.CompletedTask;
    }
}