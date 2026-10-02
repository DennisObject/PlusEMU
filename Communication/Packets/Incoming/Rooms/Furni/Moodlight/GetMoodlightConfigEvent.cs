using Plus.Communication.Packets.Outgoing.Rooms.Furni.Moodlight;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Moodlight;

internal class GetMoodlightConfigEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, true))
            return Task.CompletedTask;
        if (room.MoodlightData == null)
        {
            foreach (var item in room.GetRoomItemHandler().GetWall.ToList())
            {
                if (!item.IsTemporary && item.Definition.InteractionType == InteractionType.Moodlight)
                    room.MoodlightData = new(item.Id);
            }
        }
        if (room.MoodlightData == null || room.GetRoomItemHandler().GetItem(room.MoodlightData.ItemId)?.IsTemporary == true)
            return Task.CompletedTask;
        session.Send(new MoodlightConfigComposer(room.MoodlightData));
        return Task.CompletedTask;
    }
}