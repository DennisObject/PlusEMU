namespace Plus.HabboHotel.Rooms;

using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.Database;
using Plus.Core.Settings;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Instance;

public sealed class RoomRuntimeComponent(IRoomItemStore itemStore, IRoomUserStore userStore,
    IRoomUserSnapshotService userSnapshots, TimeProvider clock, IWiredRoomSettingsFactory wiredSettings,
    IWiredConfigurationStore wiredConfigurations, IDatabase database, IWiredRewardService wiredRewards,
    AI.IBotManagementStore botStore, ISettingsManager settings, IGroupManager groups, IGameClientManager clients) : IRoomComponent
{
    public int Order => 0;
    public void Initiate(Room room) => room.SetRuntime(
        new(room, room.Data.Model, room.NavigationLogger, settings, groups), new(room, itemStore), new(room, userStore, clock),
        new(room, room.WiredLogger, clock, settings, wiredSettings, wiredConfigurations, database, wiredRewards, botStore, clients),
        userSnapshots, clock);
    public void Initiated() { }
}
