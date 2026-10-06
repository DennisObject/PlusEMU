using System.Reflection;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Tests;

internal sealed class TestRoomAchievements(Action<GameClient, string, int>? progress = null) : IAchievementManager
{
    public static TestRoomAchievements Unused { get; } = new();
    public Dictionary<string, Achievement> Achievements => throw new InvalidOperationException("Unexpected achievement lookup.");
    public Task Init() => throw new InvalidOperationException("Unexpected achievement initialization.");
    public ICollection<Achievement> GetGameAchievements(int gameId) => throw new InvalidOperationException("Unexpected game achievements.");
    public bool ProgressAchievement(GameClient session, string group, int amount, bool fromBeginning = false)
    {
        if (progress == null) {
            throw new InvalidOperationException("Unexpected room achievement.");
        }

        progress(session, group, amount);

        return true;
    }
}

public class TestRoomOwners : DispatchProxy
{
    private Action<uint>? _unload;
    public static IRoomManager Unused { get; } = Create(null);
    public static IRoomManager Create(Action<uint>? unload)
    {
        var owner = Create<IRoomManager, TestRoomOwners>();
        ((TestRoomOwners)owner)._unload = unload;

        return owner;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != nameof(IRoomManager.UnloadRoom) || _unload == null) {
            throw new InvalidOperationException($"Unexpected room manager call: {method?.Name}.");
        }

        _unload((uint)args![0]!);

        return null;
    }
}
