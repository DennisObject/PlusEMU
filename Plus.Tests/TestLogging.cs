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
        Plus.Core.ExceptionLogger.Configure(Factory);
        Plus.Core.ConsoleCommands.Configure(Factory);
    }

    internal static ILogger Logger => NullLogger.Instance;
    internal static ILogger<GameClient> GameClient => NullLogger<GameClient>.Instance;
    internal static ILogger<RoomNavigation> Navigation => NullLogger<RoomNavigation>.Instance;
    internal static ILoggerFactory Factory => NullLoggerFactory.Instance;
    internal static ILogger<T> For<T>() => NullLogger<T>.Instance;
}

internal sealed class TestRoomFactory : IRoomFactory
{
    public Room Create(RoomData data) => throw new NotSupportedException();
    public void Dispose(uint roomId) { }
}
