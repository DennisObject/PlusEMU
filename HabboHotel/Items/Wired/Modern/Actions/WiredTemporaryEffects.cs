using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>One lease per actual room avatar, shared across boxes. Scheduling stays in the engine.</summary>
public sealed class WiredTemporaryEffects
{
    private sealed record Lease(long Generation, int Original);
    private readonly Dictionary<RoomUser, Lease> _leases = [];
    private long _generation;
    private static readonly ConditionalWeakTable<Room, WiredTemporaryEffects> Rooms = new();
    public static WiredTemporaryEffects For(Room room) => Rooms.GetValue(room, _ => new());

    public Action Acquire(RoomUser user, Func<int> readEffect, Action<int> writeEffect, Func<bool> stillAttached)
    {
        var current = readEffect();
        var original = current == 4 && _leases.TryGetValue(user, out var previous) ? previous.Original : current;
        var lease = new Lease(++_generation, original);
        _leases[user] = lease;
        writeEffect(4);
        return () =>
        {
            if (!_leases.TryGetValue(user, out var live) || live.Generation != lease.Generation) return;
            _leases.Remove(user);
            if (stillAttached() && readEffect() == 4) writeEffect(lease.Original);
        };
    }
}
