using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Rooms;

namespace Plus.Tests;

internal static class TestLogging
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Configure()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        Dapper.SqlMapper.AddTypeHandler(new Plus.Database.UtcDateTimeOffsetHandler());
        Plus.Core.ExceptionLogger.Configure(Factory);
        Plus.Core.ConsoleCommands.Configure(Factory);
    }

    internal static ILogger Logger => NullLogger.Instance;
    internal static ILogger<GameClient> GameClient => NullLogger<GameClient>.Instance;
    internal static ILogger<RoomNavigation> Navigation => NullLogger<RoomNavigation>.Instance;
    internal static ILogger<WiredRewardService> Rewards => NullLogger<WiredRewardService>.Instance;
    internal static ILoggerFactory Factory => NullLoggerFactory.Instance;
    internal static ILogger<T> For<T>() => NullLogger<T>.Instance;
}

internal sealed class TestRoomFactory : IRoomFactory
{
    public Room Create(RoomData data) => throw new NotSupportedException();
    public void Dispose(uint roomId) { }
}

internal sealed class TestRoomItemStore : IRoomItemStore
{
    internal static TestRoomItemStore Instance { get; } = new();
    public void AssignOwner(uint itemId, int userId) { }
    public void ClearRoom(uint itemId) { }
    public void SaveWallPosition(uint itemId, string wallPosition) { }
    public void SaveMoved(IReadOnlyList<RoomItemSave> items) { }
    public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) { }
    public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) { }
}

internal sealed class TestRoomUserStore : IRoomUserStore
{
    internal static TestRoomUserStore Instance { get; } = new();
    public void UpdateUserCount(uint roomId, int count) { }
    public void SavePet(RoomPetSave pet) { }
    public void SaveBot(RoomBotSave bot) { }
    public void RecordExit(uint roomId, int userId, DateTimeOffset exitedAt, int usersNow) { }
}

internal sealed class TestRoomDataLoaderFactory : IRoomDataLoaderFactory
{
    public IRoomDataLoader Create(IRoomManager rooms) => new RoomDataLoader(
        EditorTestSupport.UntouchableDatabase(), rooms, null!, null!);
}
