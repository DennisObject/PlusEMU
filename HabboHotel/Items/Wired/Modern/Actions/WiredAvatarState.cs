using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Tracks only Wired-owned freeze state; room lifecycle must forget departing avatars.</summary>
public sealed class WiredAvatarState
{
    private sealed record Freeze(bool Frozen, bool CanWalk, int Effect, bool CancelOnTeleport);
    private readonly Dictionary<RoomUser, Freeze> _frozen = [];
    private readonly Room? _room;
    public WiredAvatarState(Room? room = null) => _room = room;
    private static readonly ConditionalWeakTable<Room, WiredAvatarState> Rooms = new();
    public static WiredAvatarState For(Room room) => Rooms.GetValue(room, value => new(value));
    public bool FreezeUser(RoomUser user, int effect, bool cancelOnTeleport)
    {
        if (_room?.GetGameMap()?.Navigation is { UsesExecutor: true } navigation)
        {
            navigation.RunOwner(user, (actor, sequence) =>
            {
                navigation.CancelThrough(actor, sequence);
                FreezeActor(actor, effect, cancelOnTeleport);
            });
        }
        else FreezeActor(user, effect, cancelOnTeleport);
        return true;
    }
    private void FreezeActor(RoomUser user, int effect, bool cancelOnTeleport)
    {
        var original = _frozen.GetValueOrDefault(user);
        _frozen[user] = new(original?.Frozen ?? user.Frozen, original?.CanWalk ?? user.CanWalk, effect, cancelOnTeleport);
        user.IsWalking = false; user.PathRecalcNeeded = false; user.RemoveStatus("mv");
        user.GoalX = user.X; user.GoalY = user.Y; user.Frozen = true; user.CanWalk = false; user.UpdateNeeded = true;
        // Plus bots have no stored effect/version to restore safely. Their actual movement freeze still applies.
        if (!user.IsBot && effect > 0) user.ApplyEffect(effect);
    }
    public bool Thaw(RoomUser user, bool teleport = false)
    {
        if (!_frozen.TryGetValue(user, out var state) || teleport && !state.CancelOnTeleport) return false;
        _frozen.Remove(user); user.Frozen = state.Frozen; user.CanWalk = state.CanWalk; user.UpdateNeeded = true;
        if (!user.IsBot && state.Effect > 0 && user.GetClient()?.GetHabbo()?.Effects.CurrentEffect == state.Effect) user.ApplyEffect(0);
        return true;
    }
    public void Forget(RoomUser user) => _frozen.Remove(user);
    public void Clear() => _frozen.Clear();
}
