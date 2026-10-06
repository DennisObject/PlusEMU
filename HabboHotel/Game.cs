using Plus.Core;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Bots;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Televisions;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rewards;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Talents;

namespace Plus.HabboHotel;

// Game services need to be implemented behind an interface.
// Dependency inject the required services in the IPacketEvent
// This class will be obsolete. Do not reference to Game().<Service> but inject it instead.
public class Game : IGame
{
    private readonly IGameClientManager _clientManager;
    private readonly IItemDataManager _itemDataManager;
    private readonly ICatalogManager _catalogManager;
    private readonly INavigatorManager _navigatorManager;
    private readonly IRoomManager _roomManager;
    private readonly IChatManager _chatManager;
    private readonly IGroupManager _groupManager;
    private readonly IQuestManager _questManager;
    private readonly IAchievementManager _achievementManager;

    private IBotManager _botManager;
    private ICacheManager _cacheManager;
    private readonly int _cycleSleepTime = 25;
    private IGameDataManager _gameDataManager;
    private volatile bool _cycleActive;
    private readonly object _cycleSync = new();
    private Task? _gameCycle;

    public Game(
        IGameClientManager gameClientManager,
        IItemDataManager itemDataManager,
        ICatalogManager catalogManager,
        INavigatorManager navigatorManager,
        IRoomManager roomManager,
        IChatManager chatManager,
        IGroupManager groupManager,
        IQuestManager questManager,
        IAchievementManager achievementManager,
        IGameDataManager gameDataManager,
        IBotManager botManager,
        ICacheManager cacheManager)
    {
        _clientManager = gameClientManager;
        _itemDataManager = itemDataManager;
        _catalogManager = catalogManager;
        _navigatorManager = navigatorManager;
        _roomManager = roomManager;
        _chatManager = chatManager;
        _groupManager = groupManager;
        _questManager = questManager;
        _achievementManager = achievementManager;
        _gameDataManager = gameDataManager;
        _botManager = botManager;
        _cacheManager = cacheManager;
    }

    public void StartGameLoop()
    {
        lock (_cycleSync)
        {
            if (_gameCycle != null)
                throw new InvalidOperationException("The game loop has already been started.");
            _cycleActive = true;
            _gameCycle = Task.Run(GameCycle);
        }
    }

    private void GameCycle()
    {
        while (_cycleActive)
        {
            _roomManager.OnCycle();
            _clientManager.OnCycle();
            Thread.Sleep(_cycleSleepTime);
        }
    }

    public void StopGameLoop()
    {
        lock (_cycleSync)
        {
            _cycleActive = false;
            try
            {
                _gameCycle?.GetAwaiter().GetResult();
            }
            finally
            {
                _gameCycle = null;
            }
        }
    }

    public IGameClientManager ClientManager => _clientManager;

    public ICatalogManager Catalog => _catalogManager;

    public INavigatorManager Navigator => _navigatorManager;

    public IItemDataManager ItemManager => _itemDataManager;

    public IRoomManager RoomManager => _roomManager;

    public IAchievementManager AchievementManager => _achievementManager;


    public IQuestManager QuestManager => _questManager;

    public IGroupManager GroupManager => _groupManager;

    public IChatManager ChatManager => _chatManager;

    public IGameDataManager GameDataManager => _gameDataManager;

    public IBotManager BotManager => _botManager;

    public ICacheManager CacheManager => _cacheManager;
}
