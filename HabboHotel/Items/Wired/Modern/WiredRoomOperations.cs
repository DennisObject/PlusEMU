using System.Drawing;
using System.Collections.Immutable;
using System.Globalization;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern;

public sealed record WiredCollisionPolicy(IReadOnlySet<uint> ThroughFurni, IReadOnlySet<int> ThroughUsers,
    IReadOnlySet<uint> BlockingFurni)
{
    public bool BlocksUsers(IEnumerable<RoomUser> users) => users.Any(user => !ThroughUsers.Contains(user.VirtualId));
    public bool BlocksFurni(Item item) => BlockingFurni.Contains(item.Id)
        || !item.Definition.Stackable && !ThroughFurni.Contains(item.Id);
}

/// <summary>Movement and queries share the same footprint and placement checks.</summary>
public static class WiredRoomOperations
{
    /// <summary>Save preparation only: capture current picked items before pure validation. Never call while loading.</summary>
    public static WiredConfiguration PrepareSnapshots(IWiredConfiguredItem box, WiredConfiguration proposed)
    {
        if (box.Descriptor.CanonicalName is not ("wf_act_match_to_sshot" or "wf_act_place_furni"
            or "wf_cnd_match_snapshot" or "wf_cnd_not_match_snap" or "wf_trg_stuff_state" or "wf_trg_state_changed")) return proposed;
        var handler = box.Instance.GetRoomItemHandler();
        var picked = proposed.SelectedItems.Distinct().Select(handler.GetItem)
            .Where(item => item is { IsFloorItem: true }).ToArray();
        var templates = box.Descriptor.CanonicalName == "wf_act_place_furni" && proposed.TemporaryPlacement != null;
        return proposed with {
            Snapshots = templates && picked.Length == 0 ? proposed.Snapshots : picked.Select(Capture).ToImmutableArray(),
            SelectedItems = templates ? proposed.SelectedItems.Where(id => handler.GetItem(id)?.IsTemporary != true).ToImmutableArray() : proposed.SelectedItems,
            SecondarySelectedItems = templates ? proposed.SecondarySelectedItems.Where(id => handler.GetItem(id)?.IsTemporary != true).ToImmutableArray() : proposed.SecondarySelectedItems
        };
    }
    public static Point Offset(int direction) => direction switch
    {
        0 => new(0, -1), 1 => new(1, -1), 2 => new(1, 0), 3 => new(1, 1),
        4 => new(0, 1), 5 => new(-1, 1), 6 => new(-1, 0), 7 => new(-1, -1),
        _ => Point.Empty
    };

    public static IEnumerable<Point> Footprint(Item item, int x, int y, int rotation)
    {
        // Gamemap.GetAffectedTiles omits the origin and can repeat other cells.
        yield return new(x, y);
        foreach (var point in Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width,
                     x, y, rotation).Values.Select(tile => new Point(tile.X, tile.Y)).Distinct())
            yield return point;
    }

    public static bool IsOnItem(RoomUser user, Item item) =>
        Footprint(item, item.GetX, item.GetY, item.Rotation).Contains(user.Coordinate);

    public static bool HasStackedItem(Room room, Item item) =>
        Footprint(item, item.GetX, item.GetY, item.Rotation)
            .SelectMany(point => room.GetGameMap().GetCoordinatedItems(point))
            .Any(other => other.Id != item.Id && other.GetZ >= item.TotalHeight);

    public static bool CanMoveItem(Room room, Item item, int x, int y, int rotation,
        double? height = null, bool throughUsers = false, bool throughFurni = false, WiredCollisionPolicy? collision = null)
    {
        return room.GetRoomItemHandler().GetItem(item.Id) == item
            && CanPlaceItem(room, item, x, y, rotation, height, throughUsers, throughFurni, collision);
    }

    public static bool CanPlaceItem(Room room, Item item, int x, int y, int rotation,
        double? height = null, bool throughUsers = false, bool throughFurni = false, WiredCollisionPolicy? collision = null)
    {
        if (!item.IsFloorItem || !ValidRotation(item, rotation)
            || height is { } z && (!double.IsFinite(z) || z < 0 || z > 80)) return false;
        var map = room.GetGameMap();
        var magic = MagicTileHeight.IsMagicTile(item.Definition.InteractionType);
        foreach (var point in Footprint(item, x, y, rotation))
        {
            if (!map.ValidTile(point.X, point.Y)) return false;
            if (magic && point.X == map.Model.DoorX && point.Y == map.Model.DoorY) return false;
            var placement = map.ResolvePlacement(point.X, point.Y, item.Id, collision);
            if (!magic && !placement.HasHelper && map.Model.SqState[point.X, point.Y] != SquareState.Open) return false;
            if (!magic && !placement.HasHelper && !throughUsers && !item.Definition.IsSeat && (collision?.BlocksUsers(map.GetRoomUsers(point)) ?? map.GetRoomUsers(point).Count != 0))
                return false;
            var others = map.GetCoordinatedItems(point).Where(other => other.Id != item.Id).ToArray();
            if (others.Any(other => collision?.BlockingFurni.Contains(other.Id) == true)
                || !magic && !placement.HasHelper && !throughFurni && others.Any(other => collision?.BlocksFurni(other) ?? !other.Definition.Stackable))
                return false;
            var top = height ?? placement.PlacementZ;
            if (top + item.Definition.Height > 80)
                return false;
        }
        return true;
    }

    public static bool ValidRotation(Item item, int rotation) =>
        rotation is >= 0 and <= 7 && (item.Definition.ExtraRot || rotation % 2 == 0);

    public static bool MoveItem(Room room, Item item, int x, int y, int? rotation = null,
        double? height = null, bool keepAltitude = false, bool animate = true, WiredCollisionPolicy? collision = null, bool announce = true)
    {
        var rot = rotation ?? item.Rotation;
        var z = height ?? (keepAltitude ? item.GetZ : (double?)null);
        if (!CanMoveItem(room, item, x, y, rot, z, collision: collision))
            return false;
        var source = new Point(item.GetX, item.GetY);
        var sourceZ = item.GetZ;
        var rotationChanged = rot != item.Rotation;
        if (source.X == x && source.Y == y && rot == item.Rotation && (z == null || z == sourceZ))
            return false;
        // The full placement path maintains map/index, moved-item persistence and room statuses.
        if (!room.GetRoomItemHandler().SetFloorItem(null!, item, x, y, rot, false, false,
                announce && (!animate || rotationChanged), true, z ?? -1, collision))
            return false;
        if (animate)
            room.SendPacket(new SlideObjectBundleComposer(source.X, source.Y, sourceZ,
                item.GetX, item.GetY, item.GetZ, 0, 0, item.Id));
        return true;
    }

    public static bool RelocateAvatar(Room room, RoomUser avatar, int x, int y,
        bool slide, bool throughUsers = false)
    {
        var map = room.GetGameMap();
        if (room.GetRoomUserManager().GetRoomUserByVirtualId(avatar.VirtualId) != avatar
            || !map.ValidTile(x, y) || map.Model.SqState[x, y] != SquareState.Open)
            return false;
        if (avatar.X == x && avatar.Y == y)
            return false;
        if (!throughUsers && (!map.CanWalk(x, y, false)
                             || map.GetRoomUsers(new(x, y)).Any(other => other != avatar)))
            return false;

        avatar.ClearMovement(true);
        var source = avatar.Coordinate;
        var oldZ = avatar.Z;
        var z = map.SqAbsoluteHeight(x, y);
        // Relocation changes both occupancy indexes before statuses are rebuilt.
        map.UpdateUserMovement(source, new(x, y), avatar);
        map.GameMap[source.X, source.Y] = avatar.SqState;
        avatar.SqState = map.GameMap[x, y];
        avatar.SetPos(x, y, z);
        map.GameMap[x, y] = 1;
        avatar.GoalX = x;
        avatar.GoalY = y;
        avatar.UpdateNeeded = true;
        room.GetRoomUserManager().UpdateUserStatus(avatar, true);
        if (slide)
            room.SendPacket(new SlideObjectBundleComposer(source.X, source.Y, oldZ, x, y,
                avatar.Z, 0, avatar.VirtualId, 0));
        return true;
    }

    public static WiredFurniSnapshot Capture(Item item) => new(item.Id, item.Definition.Id,
        item.GetX, item.GetY, item.GetZ, item.Rotation, item.LegacyDataString ?? string.Empty);

    public static bool MatchesSnapshot(Item item, WiredFurniSnapshot snapshot,
        bool state, bool direction, bool position, bool altitude) =>
        (!state || string.Equals(item.LegacyDataString ?? string.Empty, snapshot.State, StringComparison.Ordinal))
        && (!direction || item.Rotation == snapshot.Rotation)
        && (!position || item.GetX == snapshot.X && item.GetY == snapshot.Y)
        && (!altitude || Math.Abs(item.GetZ - snapshot.Z) < 0.00001);

    public static bool TryAltitude(string text, out double height) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out height)
        && double.IsFinite(height) && height is >= 0 and <= 80;

    public static bool Quantify(IEnumerable<bool> outcomes, int quantifier)
    {
        var found = false;
        foreach (var outcome in outcomes)
        {
            if (quantifier == 0 && !outcome) return false;
            if (quantifier != 0 && outcome) return true;
            found = true;
        }
        return quantifier == 0 && found;
    }

    /// <summary>Polaris three-way editor comparison: 0 less, 1 equal, 2 greater.</summary>
    public static bool Compare(long actual, long expected, int comparison) => comparison switch
    {
        0 => actual < expected, 1 => actual == expected, 2 => actual > expected, _ => false
    };
}
