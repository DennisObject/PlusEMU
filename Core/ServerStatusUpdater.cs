using Microsoft.Extensions.Logging;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Core;

public class ServerStatusUpdater : IDisposable, IServerStatusUpdater, IStartable
{
    private const int UpdateInSeconds = 30;
    private readonly ILogger<ServerStatusUpdater> _logger;
    private readonly IGameClientManager _gameClientManager;
    private readonly IRoomManager _roomManager;
    private readonly IServerUptime _uptime;

    public ServerStatusUpdater(ILogger<ServerStatusUpdater> logger, IGameClientManager gameClientManager, IRoomManager roomManager, IServerUptime uptime)
    {
        _logger = logger;
        _gameClientManager = gameClientManager;
        _roomManager = roomManager;
        _uptime = uptime;
    }

    private Timer? _timer;

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    public int StartOrder => 90;
    public Task Start()
    {
        Init();

        return Task.CompletedTask;
    }

    public void Init()
    {
        _timer = new(OnTick, null, TimeSpan.FromSeconds(UpdateInSeconds), TimeSpan.FromSeconds(UpdateInSeconds));
        ConsoleWindow.SetTitle("Plus Emulator - 0 users online - 0 rooms loaded - 0 day(s) 0 hour(s) uptime");
        _logger.LogInformation("Server Status Updater has been started.");
    }

    public void OnTick(object? obj)
    {
        UpdateOnlineUsers();
    }

    private void UpdateOnlineUsers()
    {
        var uptime = _uptime.Elapsed;
        var usersOnline = _gameClientManager.Count;
        var roomCount = _roomManager.Count;
        ConsoleWindow.SetTitle($"Plus Emulator - {usersOnline} users online - {roomCount} rooms loaded - {uptime.Days} day(s) {uptime.Hours} hour(s) uptime");
    }
}
