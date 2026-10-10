using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items.Wired.Variables;

internal readonly record struct WiredWallAltitudeInputs(int? NativeAltitude, double HalfScale, double TileTop,
    int LocalX, double PixelY, bool Left)
{
    internal double WorldAltitude => NativeAltitude is { } altitude ? altitude / 100.0
        : TileTop - (PixelY - (Left ? (HalfScale - LocalX) / 2 : LocalX / 2.0)) / HalfScale;
    internal long Hundredths => NativeAltitude is { } altitude ? altitude : (long)Math.Floor(WorldAltitude * 100 + 0.5);
}

internal sealed record WiredWallBuiltinInput(uint ItemId, int SpriteId, WiredWallSnapshot Position, WiredWallAltitudeInputs Altitude);

internal sealed record WiredWallInspectionSnapshot(Room Room, Gamemap Map, RoomModel StaticModel, Item Item,
    ItemDefinition Definition, WiredVariableHolder Holder, WiredRuntimeContext Context, string RawWallCoordinates, WiredWallBuiltinInput Values)
{
    internal bool Matches(Room room, Item current, WiredVariableHolder holder, WiredVariableFrame frame) =>
        holder.Target == WiredVariableTarget.Furni && holder == Holder && frame.Contains(holder) && frame.RoomId == Room.Id
        && ReferenceEquals(room, Room) && ReferenceEquals(frame.RuntimeContext, Context) && Context.Event.Kind == WiredEventKind.Inspection
        && ReferenceEquals(Context.Room, Room) && Context.FurniIdentity.TryGetValue(Values.ItemId, out var captured)
        && ReferenceEquals(captured, Item) && ReferenceEquals(current, Item)
        && ReferenceEquals(Room.GetRoomItemHandler().GetItem(Values.ItemId), Item)
        && Item.Id == Values.ItemId && Item.RoomId == Room.Id && Item.IsWallItem
        && WiredVariableRuntimeFrames.FurniHolder(Item) == Holder && ReferenceEquals(Item.Definition, Definition)
        && Definition.Type == ItemType.Wall && Definition.SpriteId == Values.SpriteId
        && ReferenceEquals(Room.GetGameMap(), Map) && ReferenceEquals(Map.StaticModel, StaticModel);
}

internal static class WiredWallBuiltinValues
{
    internal static readonly IReadOnlyList<string> Tokens = Array.AsReadOnly(new[]
    {
        "@id", "@class_id", "@position.x", "@position.y", "@position", "@occupation", "@rotation", "@altitude", "@wallitem_offset"
    });
    internal static bool Supports(string key) => Tokens.Contains(key);
    internal static long? ReadIdentity(uint itemId, int spriteId, string key) => key switch
    {
        "@id" => -(long)itemId,
        "@class_id" => -(long)spriteId,
        _ => null
    };
    internal static long? ReadPlacement(WiredWallSnapshot position, WiredWallAltitudeInputs altitude, string key) => key switch
    {
        "@position.x" => position.TileX,
        "@position.y" => position.TileY,
        "@position" => unchecked((position.TileX << 8) | position.TileY),
        "@occupation" => unchecked((position.TileX << 16) | (position.TileY << 8) | (position.Left ? 0 : 1)),
        "@rotation" => position.Left ? 0 : 1,
        "@wallitem_offset" => position.LocalX,
        "@altitude" => altitude.Hundredths,
        _ => null
    };
    internal static bool TryReadInspection(Room room, Item current, string key, WiredVariableHolder holder,
        WiredVariableFrame frame, out long? value)
    {
        value = null;

        if (frame.WallInspectionSnapshot is not { } snapshot || !Supports(key)) {
            return false;
        }

        if (snapshot.Matches(room, current, holder, frame)) {
            value = ReadIdentity(snapshot.Values.ItemId, snapshot.Values.SpriteId, key)
                ?? ReadPlacement(snapshot.Values.Position, snapshot.Values.Altitude, key);
        }

        // A rejected captured input is handled, so it cannot fall back to live placement.
        return true;
    }
}
