using Plus.HabboHotel.Items;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    internal int? ReadBuiltin(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        if (frame.RoomId != _room.Id || !frame.Contains(holder)) return null;
        var key = RoomWiredBuiltinVariables.Normalize(reference.Token);
        if (holder.Target == WiredVariableTarget.User)
        {
            var user = _room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);
            if (user == null || user.IsBot || WiredVariableRuntimeFrames.UserHolder(user) != holder
                || frame.RuntimeContext is { } userContext && (!userContext.UserIdentity.TryGetValue(user.VirtualId, out var visit)
                    || !ReferenceEquals(visit, user))) return null;
            return key switch
            {
                "@room_entry.method" => (int)user.WiredRoomEntry.Method,
                "@room_entry.teleport_id" => unchecked((int)user.WiredRoomEntry.TeleporterId),
                _ => null
            };
        }
        if (holder.Target != WiredVariableTarget.Furni) return null;
        var item = _room.GetRoomItemHandler().GetItem(unchecked((uint)holder.EntityId));
        if (item == null || WiredVariableRuntimeFrames.FurniHolder(item) != holder
            || frame.RuntimeContext is { } itemContext && (!itemContext.FurniIdentity.TryGetValue(item.Id, out var captured)
                || !ReferenceEquals(captured, item))) return null;
        if (item.IsWallItem && ReadWall(item, out var position))
            return key switch
            {
                "@position.x" => position.X, "@position.y" => position.Y,
                "@wallitem_offset" => position.Offset, "@altitude" => position.Altitude * 100,
                "@rotation" => position.Left ? 4 : 6, _ => null
            };
        return WiredProjectileFlights.For(_room).Read(item, key,
            frame.RuntimeContext?.NowMilliseconds ?? _engine.NowMilliseconds);
    }

    public void RecordRoomNetworkForward(RoomUser actor, uint destinationRoomId) => _engine.Mutate(() =>
    {
        if (actor.IsBot || !ReferenceEquals(_room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId), actor)) return false;
        var player = actor.GetClient()?.GetHabbo();
        if (player == null) return false;
        player.WiredRoomNetworkDestination = player.IsTeleporting ? 0 : destinationRoomId;
        return true;
    });

    private bool ReadWall(Item item, out WiredWallPosition position) =>
        WiredWallPosition.TryParse(_room.GetRoomItemHandler().WallPositionCheck(item.WallCoordinates), out position);

    private bool WriteWall(Item item, string key, int value)
    {
        if (!ReadWall(item, out var position)) return false;
        WiredWallPosition next;
        switch (key)
        {
            case "@position.x": next = position with { X = value }; break;
            case "@position.y": next = position with { Y = value }; break;
            case "@wallitem_offset": next = position with { Offset = value }; break;
            case "@altitude" when value % 100 == 0: next = position with { Altitude = value / 100 }; break;
            case "@rotation" when value is 4 or 6: next = position with { Left = value == 4 }; break;
            default: return false;
        }
        var validated = _room.GetRoomItemHandler().WallPositionCheck(next.ToString());
        if (validated == null || next == position) return false;
        item.WallCoordinates = validated;
        _room.GetRoomItemHandler().UpdateItem(item);
        _room.SendPacket(new ItemUpdateComposer(item));
        return true;
    }

    internal bool WriteBuiltin(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame)
    {
        var context = frame.RuntimeContext;
        if (context == null || !ReferenceEquals(context.Room, _room)) return false;
        var key = RoomWiredBuiltinVariables.Normalize(reference.Token);
        var movement = new WiredRoomMovement(DispatchWalkTransition);
        if (holder.Target == WiredVariableTarget.Furni)
        {
            var item = _room.GetRoomItemHandler().GetItem(unchecked((uint)holder.EntityId));
            if (item == null || WiredVariableRuntimeFrames.FurniHolder(item) != holder
                || !context.FurniIdentity.TryGetValue(item.Id, out var captured) || !ReferenceEquals(captured, item)) return false;
            if (item.IsWallItem) return WriteWall(item, key, value);
            if (!item.IsFloorItem) return false;
            return key switch
            {
                "@position.x" => movement.MoveFurniture(context, item, value, item.GetY, item.Rotation, null),
                "@position.y" => movement.MoveFurniture(context, item, item.GetX, value, item.Rotation, null),
                "@rotation" when value is >= 0 and <= 7 => movement.MoveFurniture(context, item, item.GetX, item.GetY, value, null),
                "@altitude" => movement.MoveFurniture(context, item, item.GetX, item.GetY, item.Rotation, value / 100.0),
                _ => false
            };
        }
        if (holder.Target == WiredVariableTarget.User)
        {
            var user = _room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);
            if (user == null || WiredVariableRuntimeFrames.UserHolder(user) != holder
                || !context.UserIdentity.TryGetValue(user.VirtualId, out var captured) || !ReferenceEquals(captured, user)) return false;
            return key switch
            {
                "@position.x" => movement.MoveAvatar(context, user, value, user.Y, true),
                "@position.y" => movement.MoveAvatar(context, user, user.X, value, true),
                "@direction" when value is >= 0 and <= 7 => Rotate(user, value),
                _ => false
            };
        }
        return false;
    }

    // Module.Change invokes this completion only after releasing its value lock.
    internal void PublishBuiltinStateChanged(Item item, WiredVariableFrame frame) => _engine.Mutate(() =>
    {
        if (frame.RoomId != _room.Id || !_targets.IsAttached(item)) return false;
        var context = frame.RuntimeContext;
        var actor = context?.Event.Kind == WiredEventKind.Leave ? null : context?.Event.Actor;
        if (actor != null && !ReferenceEquals(_room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId), actor)) return false;
        QueueRuntimeEvent(new(WiredEventKind.StateChanged) { EventItem = item, Actor = actor }, frame.Depth + 1);
        return true;
    });

    private static bool Rotate(RoomUser user, int direction)
    {
        user.SetRot(direction, false);
        user.UpdateNeeded = true;
        return true;
    }
}
