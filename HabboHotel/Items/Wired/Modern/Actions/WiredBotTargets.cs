using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Actual target identities for bot movement/following and one arrival per approach.</summary>
public sealed class WiredBotTargets
{
    private sealed class Target(Item? item, RoomUser? user)
    {
        public Item? Item = item; public RoomUser? User = user; public bool Reached;
    }
    private readonly Dictionary<RoomUser, Target> _targets = [];
    private static readonly ConditionalWeakTable<Room, WiredBotTargets> Rooms = new();
    public static WiredBotTargets For(Room room) => Rooms.GetValue(room, _ => new());
    public bool HasTargets => _targets.Count != 0;
    public void Walk(RoomUser bot, Item item) => _targets[bot] = new(item, null);
    public void Follow(RoomUser bot, RoomUser user) => _targets[bot] = new(null, user);
    public void Forget(RoomUser user)
    {
        _targets.Remove(user);

        foreach (var bot in _targets.Where(entry => ReferenceEquals(entry.Value.User, user)).Select(entry => entry.Key).ToArray())
        {
            Stop(bot);
        }
    }
    public void Clear() => _targets.Clear();
    public void Stop(RoomUser bot)
    {
        _targets.Remove(bot);
        bot.BotData.ForcedMovement = false;
        bot.BotData.ForcedUserTargetMovement = 0;
        bot.ClearMovement(true);
    }
    public IReadOnlyList<WiredRuntimeEvent> Poll(Room room)
    {
        var result = new List<WiredRuntimeEvent>();
        var attached = room.GetRoomUserManager().GetUserList().ToHashSet();

        foreach (var (bot, target) in _targets.ToArray())
        {
            if (!attached.Contains(bot))
            {
                _targets.Remove(bot);
                continue;
            }

            if (target.Item is { } item)
            {
                if (!ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item))
                {
                    Stop(bot);
                    continue;
                }

                if (!WiredRoomOperations.IsOnItem(bot, item))
                {
                    continue;
                }

                result.Add(new(WiredEventKind.BotReachedFurni)
                {
                    Actor = bot,
                    EventItem = item
                });
                Stop(bot);
            }
            else if (target.User is { } user)
            {
                if (!attached.Contains(user))
                {
                    Stop(bot);
                    continue;
                }

                var near = Math.Max(Math.Abs(user.X - bot.X), Math.Abs(user.Y - bot.Y)) <= 1;

                if (near && !target.Reached)
                {
                    result.Add(new(WiredEventKind.BotReachedUser)
                    {
                        Actor = bot,
                        TargetUser = user
                    });
                }

                target.Reached = near;

                if (near)
                {
                    continue;
                }

                var destinations = Enumerable.Range(0, 4).Select(direction => WiredRoomOperations.Offset(direction * 2))
                    .Select(offset => (X: user.X + offset.X, Y: user.Y + offset.Y))
                    .Where(point => room.GetGameMap().ValidTile(point.X, point.Y) && room.GetGameMap().CanWalk(point.X, point.Y, false))
                    .OrderBy(point => Math.Abs(point.X - bot.X) + Math.Abs(point.Y - bot.Y)).ToArray();

                if (destinations.Length != 0)
                {
                    bot.MoveTo(destinations[0].X, destinations[0].Y);
                }
            }
        }

        return result;
    }
}
