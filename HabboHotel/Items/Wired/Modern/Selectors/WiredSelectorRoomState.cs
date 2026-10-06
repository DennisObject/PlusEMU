using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

/// <summary>Room-owned action observations; avatar identity prevents VirtualId reuse from inheriting history.</summary>
public sealed class WiredSelectorRoomState
{
    private readonly Dictionary<RoomUser, (int Action, int Parameter, long At)> _actions = [];

    public void Observe(WiredRuntimeEvent @event, long nowMilliseconds)
    {
        if (@event.Kind == WiredEventKind.AvatarAction && @event.Actor is { } user)
        {
            _actions[user] = (@event.Action, @event.Code, nowMilliseconds);
        }

        foreach (var stale in _actions.Where(x => nowMilliseconds >= x.Value.At
            && nowMilliseconds - x.Value.At > 5000).Select(x => x.Key).ToArray())
        {
            _actions.Remove(stale);
        }

        if (@event.Kind == WiredEventKind.Leave && @event.Actor is { } leaving)
        {
            _actions.Remove(leaving);
        }
    }

    public (int Action, int Parameter, long At)? Read(RoomUser user) => _actions.TryGetValue(user, out var action) ? action : null;
    public void Reset() => _actions.Clear();
}
