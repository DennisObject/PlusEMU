using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Bots;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel;

public interface IGame
{
    void StartGameLoop();
    void StopGameLoop();

    [Obsolete("Use dependency injection instead.")] IGameClientManager ClientManager { get; }

    [Obsolete("Use dependency injection instead.")] ICatalogManager Catalog { get; }

    [Obsolete("Use dependency injection instead.")] INavigatorManager Navigator { get; }

    [Obsolete("Use dependency injection instead.")] IItemDataManager ItemManager { get; }

    [Obsolete("Use dependency injection instead.")] IRoomManager RoomManager { get; }

    [Obsolete("Use dependency injection instead.")] IAchievementManager AchievementManager { get; }


    [Obsolete("Use dependency injection instead.")] IQuestManager QuestManager { get; }

    [Obsolete("Use dependency injection instead.")] IGroupManager GroupManager { get; }

    [Obsolete("Use dependency injection instead.")] IChatManager ChatManager { get; }

    [Obsolete("Use dependency injection instead.")] IGameDataManager GameDataManager { get; }

    [Obsolete("Use dependency injection instead.")] IBotManager BotManager { get; }

    [Obsolete("Use dependency injection instead.")] ICacheManager CacheManager { get; }
}