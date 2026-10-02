using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    internal int? ReadBuiltin(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        if (frame.RoomId != _room.Id || holder.Target != WiredVariableTarget.Furni || !frame.Contains(holder)) return null;
        var item = _room.GetRoomItemHandler().GetItem(unchecked((uint)holder.EntityId));
        if (item == null || WiredVariableRuntimeFrames.FurniHolder(item) != holder) return null;
        return WiredProjectileFlights.For(_room).Read(item, RoomWiredBuiltinVariables.Normalize(reference.Token),
            frame.RuntimeContext?.NowMilliseconds ?? _engine.NowMilliseconds);
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
            if (item == null || !item.IsFloorItem || WiredVariableRuntimeFrames.FurniHolder(item) != holder
                || !context.FurniIdentity.TryGetValue(item.Id, out var captured) || !ReferenceEquals(captured, item)) return false;
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

    private static bool Rotate(RoomUser user, int direction)
    {
        user.SetRot(direction, false);
        user.UpdateNeeded = true;
        return true;
    }
}
