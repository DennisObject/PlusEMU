using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;

internal class UpdateStickyNoteEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = room.GetRoomItemHandler().GetItem(packet.ReadUInt());
        if (item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.Postit)
            return Task.CompletedTask;
        var color = packet.ReadString();
        var text = packet.ReadString();
        if (!room.CheckRights(session))
        {
            if (!text.StartsWith(item.LegacyDataString))
                return Task.CompletedTask; // we can only ADD stuff! older stuff changed, this is not allowed
        }
        switch (color)
        {
            case "FFFF33":
            case "FF9CFF":
            case "9CCEFF":
            case "9CFF9C":
                break;
            default:
                return Task.CompletedTask; // invalid color
        }
        item.LegacyDataString = $"{color} {text}";
        item.UpdateState(true, true);
        return Task.CompletedTask;
    }
}