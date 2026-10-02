using Plus.Communication.Packets.Outgoing.Rooms.Furni.Stickys;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;

internal class GetStickyNoteEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = room.GetRoomItemHandler().GetItem(packet.ReadUInt());
        if (item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.Postit)
            return Task.CompletedTask;
        session.Send(new StickyNoteComposer(item.Id.ToString(), item.LegacyDataString));
        return Task.CompletedTask;
    }
}