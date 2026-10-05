using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Achievements;

namespace Plus.HabboHotel.Rooms;

public sealed class ScopedRoomFactory(IServiceScopeFactory scopeFactory, ILogger<RoomNavigation> navigationLogger, ILoggerFactory loggerFactory) : IRoomFactory, IDisposable
{
    private readonly ConcurrentDictionary<uint, IServiceScope> _scopes = new();

    public Room Create(RoomData data)
    {
        var scope = scopeFactory.CreateScope();
        var cached = false;
        try
        {
            var room = new Room(data, scope.ServiceProvider.GetServices<IRoomComponent>(), navigationLogger, loggerFactory.CreateLogger("Wired"),
                scope.ServiceProvider.GetRequiredService<IAchievementManager>(), scope.ServiceProvider.GetRequiredService<IRoomManager>());
            if (!_scopes.TryAdd(data.Id, scope))
                throw new InvalidOperationException($"A dependency scope already exists for room {data.Id}.");
            cached = true;
            room.Initiate();
            return room;
        }
        catch
        {
            if (cached)
                _scopes.TryRemove(new KeyValuePair<uint, IServiceScope>(data.Id, scope));
            scope.Dispose();
            throw;
        }
    }

    public void Dispose(uint roomId)
    {
        if (_scopes.TryRemove(roomId, out var scope))
            scope.Dispose();
    }

    public void Dispose()
    {
        foreach (var roomId in _scopes.Keys)
            Dispose(roomId);
    }
}
