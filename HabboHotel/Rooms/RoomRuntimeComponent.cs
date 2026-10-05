namespace Plus.HabboHotel.Rooms;

using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.Database;
using Plus.Core.Settings;
using Plus.Core.Language;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Emotions;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Commands;

public sealed class RoomRuntimeComponent(IRoomItemStore itemStore, Plus.HabboHotel.Items.IRoomItemMetadataStore metadata, IRoomUserStore userStore,
    IRoomUserSnapshotService userSnapshots, TimeProvider clock, IWiredRoomSettingsFactory wiredSettings,
    IWiredConfigurationStore wiredConfigurations, IDatabase database, IWiredRewardService wiredRewards,
    AI.IBotManagementStore botStore, ISettingsManager settings, IGroupManager groups, IGameClientManager clients,
    IRewardTrackManager rewards, Plus.HabboHotel.Items.IItemDataManager definitions, ILanguageManager language,
    IChatEmotionsManager chatEmotions, ICommandManager commands, IAccessControl access) : IRoomComponent
{
    public int Order => 0;
    public void Initiate(Room room) => room.SetRuntime(
        new(room, room.Data.Model, room.NavigationLogger, settings, groups, database, rewards), new(room, itemStore, metadata, clients, language), new(room, userStore, clock, rewards, chatEmotions),
        new(room, room.WiredLogger, clock, settings, wiredSettings, wiredConfigurations, database, wiredRewards, botStore, clients, groups, definitions, commands, access),
        userSnapshots, clock);
    public void Initiated() { }
}
