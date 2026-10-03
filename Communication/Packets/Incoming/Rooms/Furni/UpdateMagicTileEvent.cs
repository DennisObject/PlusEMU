using System.Drawing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class UpdateMagicTileEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        if (!room.CheckRights(session, false, true) && !session.GetHabbo().Permissions.HasRight("room_item_use_any_stack_tile"))
            return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var requestedHeight = packet.ReadInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary || !MagicTileHeight.IsMagicTile(item.Definition.InteractionType))
            return Task.CompletedTask;

        var footprint = Footprint(item);
        var floorZ = footprint.Max(tile => (double)room.GetGameMap().Model.SqFloorHeight[tile.X, tile.Y]);
        var stackBelowZ = footprint.Max(tile => StackHeightBelow(room, item, tile, floorZ));
        var height = MagicTileHeight.Resolve(requestedHeight, floorZ, stackBelowZ);

        if (!room.GetRoomItemHandler().SetFloorItem(item, item.GetX, item.GetY, height))
            return Task.CompletedTask;
        room.SendPacket(new ObjectUpdateComposer(item));
        room.SendPacket(new UpdateMagicTileComposer(itemId, MagicTileHeight.ToWire(height)));
        return Task.CompletedTask;
    }

    private static List<Point> Footprint(Item item)
    {
        var tiles = new List<Point> { item.Coordinate };
        tiles.AddRange(item.GetAffectedTiles.Values.Select(tile => new Point(tile.X, tile.Y)));
        return tiles;
    }

    private static double StackHeightBelow(Room room, Item tileItem, Point tile, double floorZ)
    {
        var highest = floorZ;
        foreach (var other in room.GetRoomItemHandler().GetFurniObjects(tile.X, tile.Y))
        {
            if (other == null || other.Id == tileItem.Id)
                continue;
            highest = Math.Max(highest, other.TotalHeight);
        }
        return highest;
    }
}

/// <summary>
/// Height rules for the stack-height widget: only magic tiles may be adjusted, "-100" matches the
/// stack below, and every result is clamped between the floor and the furniture height limit.
/// </summary>
internal static class MagicTileHeight
{
    internal const double MaximumHeight = 40.0;
    internal const int MatchBelow = -100;

    internal static bool IsMagicTile(InteractionType type) => type == InteractionType.Stacktool;

    internal static double Resolve(int requested, double floorZ, double stackBelowZ)
    {
        var height = requested == MatchBelow ? stackBelowZ : requested / 100.0;
        if (double.IsNaN(height))
            height = floorZ;
        return Math.Clamp(height, floorZ, Math.Max(floorZ, MaximumHeight));
    }

    internal static int ToWire(double height) => (int)Math.Round(height * 100.0);
}
