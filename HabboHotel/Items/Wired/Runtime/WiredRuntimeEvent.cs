using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Runtime;

public enum WiredEventKind
{
    Enter, Speech, WalkOn, WalkOff, Use, StateChanged, GameStart, GameEnd, Collision,
    Score, AvatarAction, Leave, ClickFurni, ClickTile, ClickUser, BotReachedFurni,
    BotReachedUser, Counter, Signal, Variable, Periodic, Elapsed
}

public sealed record WiredRuntimeEvent(WiredEventKind Kind)
{
    public RoomUser? Actor { get; init; }
    public Item? EventItem { get; init; }
    public RoomUser? TargetUser { get; init; }
    public string Message { get; init; } = "";
    public int Action { get; init; }
    public int Code { get; init; }
    public long PreviousValue { get; init; }
    public long Value { get; init; }
    public int Team { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}
