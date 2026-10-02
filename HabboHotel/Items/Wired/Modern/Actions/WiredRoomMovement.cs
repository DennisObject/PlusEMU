using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Mutates the actual room and emits the active renderer's animation format.</summary>
public sealed class WiredRoomMovement(Action<RoomUser, IEnumerable<Item>, IEnumerable<Item>> walkTransition)
{
    public bool MoveFurniture(WiredRuntimeContext context, Item item, int x, int y, int rotation, double? height)
    {
        var policy = context.Policy.Addons;
        var room = context.Room;
        var source = Furniture(item);
        var targetHeight = height ?? room.GetGameMap().Model.SqFloorHeight[Math.Clamp(x, 0, room.GetGameMap().Model.MapSizeX - 1), Math.Clamp(y, 0, room.GetGameMap().Model.MapSizeY - 1)];
        var options = WiredMovementPolicy.Resolve(policy, source, x, y, targetHeight, rotation, explicitHeight: height.HasValue);
        var physics = policy.Physics;
        var collision = physics == null ? null : new WiredCollisionPolicy(physics.ThroughFurni, physics.ThroughUsers, physics.BlockingFurni);
        var carried = room.GetRoomUserManager().GetRoomUsers().Where(user => policy.Carry?.UserIds.Contains(user.VirtualId) == true
            && WiredRoomOperations.IsOnItem(user, item)
            && (policy.Carry.SameTile || Math.Abs(user.Z - item.TotalHeight) < 0.001)).ToArray();
        var dx = x - item.GetX;
        var dy = y - item.GetY;
        // A carry must validate every passenger destination before the furniture commits.
        if (carried.Any(user => !ValidAvatarDestination(room, user.X + dx, user.Y + dy))) return false;
        var z = height ?? (physics?.KeepAltitude == true ? item.GetZ : (double?)null);
        if (!WiredRoomOperations.MoveItem(room, item, x, y, options.Rotation, z, animate: false,
                collision: collision, announce: !options.Animate)) return false;
        if (options.Animate)
        {
            room.SendPacket(new WiredMoveStyleComposer((int)item.Id, options.CurveType,
                options.CurveType == 7 ? options.CurveStrength : options.CurveIntensity, options.AnimationDistanceOffset));
            room.SendPacket(new WiredMovementComposer(1, (int)item.Id, source.X, source.Y, source.Z,
                item.GetX, item.GetY, item.GetZ, item.Rotation, item.Rotation, options.AnimationTimeMs));
        }
        foreach (var user in carried) MoveAvatar(context, user, user.X + dx, user.Y + dy, options.Animate, 1, true);
        return true;
    }

    public bool MoveAvatar(WiredRuntimeContext context, RoomUser user, int x, int y, bool animate,
        int walkMode = 2, bool throughUsers = false)
    {
        var room = context.Room;
        var oldX = user.X; var oldY = user.Y; var oldZ = user.Z;
        var wasWalking = user.IsWalking;
        var goalX = user.GoalX; var goalY = user.GoalY;
        var oldItems = room.GetGameMap().GetCoordinatedItems(user.Coordinate).DistinctBy(item => item.Id).ToArray();
        if (!WiredRoomOperations.RelocateAvatar(room, user, x, y, false, throughUsers)) return false;
        var newItems = room.GetGameMap().GetCoordinatedItems(user.Coordinate).DistinctBy(item => item.Id).ToArray();
        walkTransition(user, oldItems, newItems);
        if (animate && !context.Policy.Addons.DisableAnimation)
        {
            var curve = context.Policy.Addons.Curve;
            room.SendPacket(new WiredMoveStyleComposer(user.VirtualId, curve?.Type ?? 0,
                curve?.Type == 7 ? curve.Strength : curve?.Intensity ?? 100, 0, true));
            room.SendPacket(new WiredMovementComposer(0, user.VirtualId, oldX, oldY, oldZ, user.X, user.Y, user.Z,
                user.RotBody, user.RotHead, context.Policy.Addons.AnimationTimeMs));
        }
        if (wasWalking && (walkMode == 1 || walkMode == 0
            && Math.Abs(goalX - x) + Math.Abs(goalY - y) < Math.Abs(goalX - oldX) + Math.Abs(goalY - oldY))) user.MoveTo(goalX, goalY);
        return true;
    }

    private static bool ValidAvatarDestination(Room room, int x, int y) => room.GetGameMap().ValidTile(x, y)
        && room.GetGameMap().Model.SqState[x, y] == SquareState.Open;
    public static WiredSelectorFurniture Furniture(Item item) => new(item.Id, (int)item.Definition.Id,
        item.Definition.PublicName, item.LegacyDataString, item.GetX, item.GetY, item.GetZ, item.TotalHeight - item.GetZ,
        WiredRoomOperations.Footprint(item, item.GetX, item.GetY, item.Rotation).Select(point => (point.X, point.Y)).ToArray(),
        item.IsFloorItem, item.IsWired);
}
