using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Core;

public class ServerStatusUpdater : IDisposable, IServerStatusUpdater
{
    private const int UpdateInSeconds = 30;
    private readonly ILogger<ServerStatusUpdater> _logger;
    private readonly IDatabase _database;
    private readonly IGameClientManager _gameClientManager;
    private readonly IRoomManager _roomManager;

    public ServerStatusUpdater(ILogger<ServerStatusUpdater> logger, IDatabase database, IGameClientManager gameClientManager, IRoomManager roomManager)
    {
        _logger = logger;
        _database = database;
        _gameClientManager = gameClientManager;
        _roomManager = roomManager;
    }

    private Timer _timer;

    public void Dispose()
    {
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.RunQuery("UPDATE `server_status` SET `users_online` = '0', `loaded_rooms` = '0'");
        }
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Init()
    {
        _timer = new(OnTick, null, TimeSpan.FromSeconds(UpdateInSeconds), TimeSpan.FromSeconds(UpdateInSeconds));
        Console.Title = "Plus Emulator - 0 users online - 0 rooms loaded - 0 day(s) 0 hour(s) uptime";
        _logger.LogInformation("Server Status Updater has been started.");
    }

    public void OnTick(object obj)
    {
        UpdateOnlineUsers();
    }

    private void UpdateOnlineUsers()
    {
        var uptime = DateTime.Now - PlusEnvironment.ServerStarted;
        var usersOnline = _gameClientManager.Count;
        var roomCount = _roomManager.Count;
        Console.Title = $"Plus Emulator - {usersOnline} users online - {roomCount} rooms loaded - {uptime.Days} day(s) {uptime.Hours} hour(s) uptime";
        using var dbClient = _database.GetQueryReactor();
        dbClient.SetQuery("UPDATE `server_status` SET `users_online` = @users, `loaded_rooms` = @loadedRooms LIMIT 1;");
        dbClient.AddParameter("users", usersOnline);
        dbClient.AddParameter("loadedRooms", roomCount);
        dbClient.RunQuery();
        // Daily online peaks feed the housekeeping dashboard.
        dbClient.SetQuery("INSERT INTO `housekeeping_online_peaks` (`day`, `peak`) VALUES (UTC_DATE(), @users) ON DUPLICATE KEY UPDATE `peak` = GREATEST(`peak`, @users);");
        dbClient.AddParameter("users", usersOnline);
        dbClient.RunQuery();
    }
}