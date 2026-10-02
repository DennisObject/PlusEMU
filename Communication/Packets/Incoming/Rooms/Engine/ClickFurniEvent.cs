using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

public sealed class ClickFurniEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int wireId, category;
        try { wireId = packet.ReadInt(); category = packet.ReadInt(); }
        catch (ArgumentException) { return Task.CompletedTask; }
        if (wireId == 0 || category is not (10 or 20) || packet.HasDataRemaining()) return Task.CompletedTask;
        var actor = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (actor == null || actor.IsBot) return Task.CompletedTask;
        // WALL uses a negative magnitude; FLOOR uses the id's uint bits, including temporary ids.
        var id = category == 20 ? (uint)Math.Abs((long)wireId) : unchecked((uint)wireId);
        var item = room.GetRoomItemHandler().GetItem(id);
        if (item == null || category == 10 && !item.IsFloorItem || category == 20 && !item.IsWallItem) return Task.CompletedTask;
        if (item.IsTemporary && !room.GetRoomItemHandler().OwnsTemporary(item)) return Task.CompletedTask;
        room.GetWired().Dispatch(new(WiredEventKind.ClickFurni) { Actor = actor, EventItem = item });
        if (category == 10 && string.Equals(item.Definition.InteractionName, "room_invisible_click_tile", StringComparison.OrdinalIgnoreCase))
            room.GetWired().Dispatch(new(WiredEventKind.ClickTile) { Actor = actor, EventItem = item, X = item.GetX, Y = item.GetY });
        return Task.CompletedTask;
    }
}
