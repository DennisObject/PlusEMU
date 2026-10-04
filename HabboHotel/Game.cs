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
public class Game : IGame, IStartable
{
    private readonly IGameClientManager _clientManager;
    private readonly IModerationManager _moderationManager;
    private readonly IItemDataManager _itemDataManager;
    private readonly ICatalogManager _catalogManager;
    private readonly ITelevisionManager _televisionManager; //TODO: Initialize from the item manager.
    private readonly INavigatorManager _navigatorManager;
    private readonly IRoomManager _roomManager;
    private readonly IChatManager _chatManager;
    private readonly IGroupManager _groupManager;
    private readonly IQuestManager _questManager;
    private readonly IAchievementManager _achievementManager;

    private IBadgeManager _badgeManager;
    private IBotManager _botManager;
    private ICacheManager _cacheManager;
    private readonly int _cycleSleepTime = 25;
    private IGameDataManager _gameDataManager;
    private IServerStatusUpdater _globalUpdater;
    private IAccessControl _permissionManager;
    private IRewardManager _rewardManager;
    private ITalentTrackManager _talentTrackManager;
    private bool _cycleActive;

    private bool _cycleEnded;
    private Task _gameCycle;

    public Game(
        IGameClientManager gameClientManager,
        IModerationManager moderationManager,
        IItemDataManager itemDataManager,
        ICatalogManager catalogManager,
        ITelevisionManager televisionManager,
        INavigatorManager navigatorManager,
        IRoomManager roomManager,
        IChatManager chatManager,
        IGroupManager groupManager,
        IQuestManager questManager,
        IAchievementManager achievementManager,
        ITalentTrackManager talentTrackManager,
        IGameDataManager gameDataManager,
        IServerStatusUpdater serverStatusUpdater,
        IBotManager botManager,
        ICacheManager cacheManager,
        IRewardManager rewardManager,
        IBadgeManager badgeManager,
        IAccessControl permissionManager)
    {
        _clientManager = gameClientManager;
        _moderationManager = moderationManager;
        _itemDataManager = itemDataManager;
        _catalogManager = catalogManager;
        _televisionManager = televisionManager;
        _navigatorManager = navigatorManager;
        _roomManager = roomManager;
        _chatManager = chatManager;
        _groupManager = groupManager;
        _questManager = questManager;
        _achievementManager = achievementManager;
        _talentTrackManager = talentTrackManager;
        _gameDataManager = gameDataManager;
        _globalUpdater = serverStatusUpdater;
        _botManager = botManager;
        _cacheManager = cacheManager;
        _rewardManager = rewardManager;
        _badgeManager = badgeManager;
        _permissionManager = permissionManager;
    }

    public Task Init()
    {
        _moderationManager.Init();
        _televisionManager.Init();
        _navigatorManager.Init();
        _roomManager.LoadModels();
        _chatManager.Init();
        _groupManager.Init();
        _questManager.Init();
        _talentTrackManager.Init();
        _gameDataManager.Init();
        _globalUpdater.Init();
        _botManager.Init();
        _rewardManager.Init();
        _badgeManager.Init();
        _permissionManager.Init();
        _cacheManager.Init();
        return Task.CompletedTask;
    }

    public int StartOrder => 100;
    public Task Start() => Init();

    public void StartGameLoop()
    {
        _gameCycle = new(GameCycle);
        _gameCycle.Start();
        _cycleActive = true;
    }

    private void GameCycle()
    {
        while (_cycleActive)
        {
            _cycleEnded = false;
            _roomManager.OnCycle();
            _clientManager.OnCycle();
            _cycleEnded = true;
            Thread.Sleep(_cycleSleepTime);
        }
    }

    public void StopGameLoop()
    {
        _cycleActive = false;
        while (!_cycleEnded) Thread.Sleep(_cycleSleepTime);
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
