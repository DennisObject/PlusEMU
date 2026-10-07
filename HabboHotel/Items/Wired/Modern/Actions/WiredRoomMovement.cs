using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Mutates the actual room and emits the active renderer's animation format.</summary>
public sealed class WiredRoomMovement(Action<RoomUser, IEnumerable<Item>, IEnumerable<Item>> walkTransition)
{
    /// <summary>A step to another tile is blocked by any other furniture unless physics moves it through; the unit's stack never is.</summary>
    public bool MoveFurniture(WiredRuntimeContext context, Item item, int x, int y, int rotation, double? height, bool step = false,
        IReadOnlySet<uint>? unit = null)
    {
        var policy = context.Policy.Addons;
        var room = context.Room;
        var source = Furniture(item);
        var targetHeight = height ?? room.GetGameMap().Model.SqFloorHeight[Math.Clamp(x, 0, room.GetGameMap().Model.MapSizeX - 1), Math.Clamp(y, 0, room.GetGameMap().Model.MapSizeY - 1)];
        var options = WiredMovementPolicy.Resolve(policy, source, x, y, targetHeight, rotation, explicitHeight: height.HasValue);
        var physics = policy.Physics;
        var collision = Collision(physics, step && (x != item.GetX || y != item.GetY), unit);
        var carried = Carried(context, item);
        var dx = x - item.GetX;
        var dy = y - item.GetY;

        // A carry must validate every passenger destination before the furniture commits.
        if (carried.Any(user => !ValidAvatarDestination(room, user.X + dx, user.Y + dy))) {
            return false;
        }

        var z = height ?? (physics?.KeepAltitude == true ? item.GetZ : (double?)null);

        if (!WiredRoomOperations.MoveItem(room, item, x, y, options.Rotation, z, animate: false,
                collision: collision, announce: !options.Animate)) {
            return false;
        }

        if (policy.Projectile?.ItemIds.Contains(item.Id) == true) {
            WiredProjectileFlights.For(room).Begin(item, source.X, source.Y, source.Z,
                options.Animate ? options.AnimationTimeMs : 0, context.NowMilliseconds);
        }

        if (options.Animate) {
            room.SendPacket(new WiredMoveStyleComposer((int)item.Id, options.CurveType,
                options.CurveType == 7 ? options.CurveStrength : options.CurveIntensity, options.AnimationDistanceOffset));
            room.SendPacket(new WiredMovementComposer(1, (int)item.Id, source.X, source.Y, source.Z,
                item.GetX, item.GetY, item.GetZ, item.Rotation, item.Rotation, options.AnimationTimeMs));
        }

        foreach (var user in carried) {
            MoveAvatar(context, user, user.X + dx, user.Y + dy, options.Animate, 1, true);
        }

        return true;
    }

    /// <summary>
    /// Resolves one execution's furniture steps together. Movers stacked on each other step as one unit, decided by its
    /// bottom item. Before a unit enters a tile, a still-pending unit there that would block it takes its own step first,
    /// so a line follows its front or waits behind it in any order.
    /// </summary>
    public bool MoveTogether(WiredRuntimeContext context, IReadOnlyList<Item> movers, Func<Item, Func<int, int, int, bool>, bool> step)
    {
        var physics = context.Policy.Addons.Physics;
        var units = Units(movers);
        var unitOf = units.SelectMany(unit => unit.Select(item => (Item: item, Unit: unit))).ToDictionary(pair => pair.Item, pair => pair.Unit);
        var pending = units.ToHashSet();
        var changed = false;

        void Step(Item[] unit)
        {
            if (pending.Remove(unit) && step(unit[0], (x, y, rotation) => Move(unit, x, y, rotation))) {
                changed = true;
            }
        }

        bool Move(Item[] unit, int x, int y, int rotation)
        {
            var collision = Collision(physics, x != unit[0].GetX || y != unit[0].GetY);

            foreach (var ahead in Destinations(unit, x, y, rotation)
                         .SelectMany(move => WiredRoomOperations.Footprint(move.Item, move.X, move.Y, move.Rotation))
                         .SelectMany(point => context.Room.GetGameMap().GetCoordinatedItems(point)).Distinct().ToArray()) {
                if (unitOf.TryGetValue(ahead, out var other) && pending.Contains(other)
                    && (collision?.BlocksFurni(ahead) ?? !ahead.Definition.Stackable)) {
                    Step(other);
                }
            }

            return MoveUnit(context, unit, x, y, rotation);
        }

        foreach (var unit in units) {
            Step(unit);
        }

        return changed;
    }

    /// <summary>
    /// Movers sharing a footprint tile, directly or through each other, in first-mover order. Each unit lists its
    /// bottom item (lowest, then lowest id) first.
    /// </summary>
    public static IReadOnlyList<Item[]> Units(IEnumerable<Item> movers)
    {
        var units = new List<List<Item>>();

        foreach (var item in movers.Distinct()) {
            var tiles = WiredRoomOperations.Footprint(item, item.GetX, item.GetY, item.Rotation).ToHashSet();
            var joined = units.Where(unit => unit.Any(other =>
                WiredRoomOperations.Footprint(other, other.GetX, other.GetY, other.Rotation).Any(tiles.Contains))).ToArray();
            var index = joined.Length == 0 ? units.Count : units.IndexOf(joined[0]);
            units.RemoveAll(joined.Contains);
            units.Insert(index, [.. joined.SelectMany(unit => unit), item]);
        }

        return units.Select(unit => unit.OrderBy(item => item.GetZ).ThenBy(item => item.Id).ToArray()).ToArray();
    }

    /// <summary>
    /// The whole unit moves or none of it does. The bottom lands as a single item would; the others keep their height
    /// above it, and every member turns by the bottom's turn.
    /// </summary>
    private bool MoveUnit(WiredRuntimeContext context, Item[] unit, int x, int y, int rotation)
    {
        var bottom = unit[0];

        if (unit.Length == 1) {
            return MoveFurniture(context, bottom, x, y, rotation, null, step: true);
        }

        var moves = Destinations(unit, x, y, rotation);
        var ids = unit.Select(item => item.Id).ToHashSet();
        var keepAltitude = context.Policy.Addons.Physics?.KeepAltitude == true;
        var collision = Collision(context.Policy.Addons.Physics, x != bottom.GetX || y != bottom.GetY, ids);

        if (moves.Any(move => !WiredRoomOperations.CanMoveItem(context.Room, move.Item, move.X, move.Y, move.Rotation,
                    keepAltitude ? move.Item.GetZ : null, collision: collision)
                || Carried(context, move.Item).Any(user => !ValidAvatarDestination(context.Room,
                    user.X + move.X - move.Item.GetX, user.Y + move.Y - move.Item.GetY)))) {
            return false;
        }

        var offsets = unit.ToDictionary(item => item, item => item.GetZ - bottom.GetZ);

        if (!MoveFurniture(context, bottom, x, y, rotation, null, step: true, ids)) {
            return false;
        }

        foreach (var move in moves.Skip(1)) {
            MoveFurniture(context, move.Item, move.X, move.Y, move.Rotation, bottom.GetZ + offsets[move.Item], step: true, ids);
        }

        return true;
    }

    private static (Item Item, int X, int Y, int Rotation)[] Destinations(Item[] unit, int x, int y, int rotation) =>
        unit.Select(item => (item, item.GetX + x - unit[0].GetX, item.GetY + y - unit[0].GetY,
            (item.Rotation + rotation - unit[0].Rotation + 8) % 8)).ToArray();

    private static RoomUser[] Carried(WiredRuntimeContext context, Item item) => context.Room.GetRoomUserManager().GetRoomUsers()
        .Where(user => context.Policy.Addons.Carry?.UserIds.Contains(user.VirtualId) == true
            && WiredRoomOperations.IsOnItem(user, item)
            && (context.Policy.Addons.Carry.SameTile || Math.Abs(user.Z - item.TotalHeight) < 0.001)).ToArray();

    public bool MoveAvatar(WiredRuntimeContext context, RoomUser user, int x, int y, bool animate,
        int walkMode = 2, bool throughUsers = false)
    {
        var room = context.Room;

        if (context.Policy.Addons.Physics is { } physics) {
            if (!ValidAvatarDestination(room, x, y)
                || room.GetGameMap().GetCoordinatedItems(new(x, y)).Any(item => physics.BlockingFurni.Contains(item.Id))) {
                return false;
            }

            var occupants = room.GetGameMap().GetRoomUsers(new(x, y)).Where(other => !ReferenceEquals(other, user)).ToArray();

            if (!throughUsers && occupants.Any(other => !physics.ThroughUsers.Contains(other.VirtualId))) {
                return false;
            }

            throughUsers |= occupants.Length > 0 && occupants.All(other => physics.ThroughUsers.Contains(other.VirtualId));
        }

        var oldX = user.X;
        var oldY = user.Y;
        var oldZ = user.Z;
        var wasWalking = user.IsWalking;
        var goalX = user.GoalX;
        var goalY = user.GoalY;
        var oldItems = room.GetGameMap().GetCoordinatedItems(user.Coordinate).DistinctBy(item => item.Id).ToArray();

        if (!WiredRoomOperations.RelocateAvatar(room, user, x, y, false, throughUsers)) {
            return false;
        }

        var newItems = room.GetGameMap().GetCoordinatedItems(user.Coordinate).DistinctBy(item => item.Id).ToArray();
        walkTransition(user, oldItems, newItems);

        if (animate && !context.Policy.Addons.DisableAnimation) {
            var curve = context.Policy.Addons.Curve;
            room.SendPacket(new WiredMoveStyleComposer(user.VirtualId, curve?.Type ?? 0,
                curve?.Type == 7 ? curve.Strength : curve?.Intensity ?? 100, 0, true));
            room.SendPacket(new WiredMovementComposer(0, user.VirtualId, oldX, oldY, oldZ, user.X, user.Y, user.Z,
                user.RotBody, user.RotHead, context.Policy.Addons.AnimationTimeMs));
        }

        if (wasWalking && (walkMode == 1 || walkMode == 0
            && Math.Abs(goalX - x) + Math.Abs(goalY - y) < Math.Abs(goalX - oldX) + Math.Abs(goalY - oldY))) {
            user.MoveTo(goalX, goalY);
        }

        return true;
    }

    internal static WiredCollisionPolicy? Collision(WiredPhysicsPolicy? physics, bool step, IReadOnlySet<uint>? unit = null) => physics == null
        ? step || unit != null ? new(new HashSet<uint>(), new HashSet<int>(), new HashSet<uint>(), step, unit) : null
        : new(physics.ThroughFurni, physics.ThroughUsers, physics.BlockingFurni, step, unit);
    private static bool ValidAvatarDestination(Room room, int x, int y) => room.GetGameMap().ValidTile(x, y)
        && room.GetGameMap().Model.SqState[x, y] == SquareState.Open;
    public static WiredSelectorFurniture Furniture(Item item) => new(item.Id, (int)item.Definition.Id,
        item.Definition.PublicName, item.LegacyDataString, item.GetX, item.GetY, item.GetZ, item.TotalHeight - item.GetZ,
        WiredRoomOperations.Footprint(item, item.GetX, item.GetY, item.Rotation).Select(point => (point.X, point.Y)).ToArray(),
        item.IsFloorItem, item.IsWired);
}
