using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.HabboHotel;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[CollectionDefinition("Wired group dependencies", DisableParallelization = true)]
public sealed class WiredGroupDependencyCollection;

[Collection("Wired group dependencies")]
public sealed class WiredGroupDependencyTests
{
    [Fact]
    public void SelectorAndAddonShareInjectedGroupSnapshotAndNextFiringRefreshesMembership()
    {
        var group = Group(7, 1);
        var lookups = 0;
        var groups = new TestGroupManager(id => { lookups++; return id == group.Id ? group : null; });
        var fixture = new Fixture(groups, group);
        var selector = fixture.Selector([0, 0, 0, 0]);
        var addon = fixture.Addon();
        var context = fixture.Context(selector.Item, addon.Item);

        using var unavailable = UnavailableGlobalGame();
        Assert.Equal([fixture.Member.VirtualId], selector.Select(context).Selection.UserIds);
        Assert.True(addon.Apply(context));
        var captured = Assert.IsType<WiredSelectorWorld>(context.SelectorWorldSnapshot);
        var before = lookups;

        group.DeleteMember(fixture.Member.HabboId);
        Assert.Equal([fixture.Member.VirtualId], selector.Select(context).Selection.UserIds);
        Assert.DoesNotContain("$(users)", context.Policy.FormatText(context, "$(users)"));
        Assert.Same(captured, context.SelectorWorldSnapshot);
        Assert.Equal(before, lookups);

        var next = fixture.Context(selector.Item, addon.Item);
        Assert.Empty(selector.Select(next).Selection.UserIds);
        Assert.True(lookups > before);

        Assert.True(selector.TryValidateConfiguration(new() { IntParams = [1, 404, 0, 0] },
            out var missing, out var error), error);
        selector.ApplyConfiguration(missing);
        Assert.Empty(selector.Select(fixture.Context(selector.Item, addon.Item)).Selection.UserIds);
    }

    [Fact]
    public void ExplicitGroupConditionUsesInjectedLiveLookupForKnownMissingAndRevokedMembership()
    {
        var group = Group(9, 1);
        Group? available = group;
        var groups = new TestGroupManager(id => id == 9 ? available : null);
        var fixture = new Fixture(groups, roomGroup: null);
        var condition = fixture.Condition([0, 1, 9, 1]);

        using var unavailable = UnavailableGlobalGame();
        Assert.True(condition.Execute(fixture.Context(condition.Item)));
        group.DeleteMember(fixture.Member.HabboId);
        Assert.False(condition.Execute(fixture.Context(condition.Item)));
        available = null;
        Assert.False(condition.Execute(fixture.Context(condition.Item)));
    }

    [Fact]
    public void RoomGroupConditionKeepsUsingTheLiveRoomGroupWithoutManagerLookup()
    {
        var group = Group(11, 1);
        var groups = new TestGroupManager(_ => throw new InvalidOperationException("Room-group mode must not query the manager."));
        var fixture = new Fixture(groups, group);
        var condition = fixture.Condition([0, 0, 0, 1]);

        using var unavailable = UnavailableGlobalGame();
        Assert.True(condition.Execute(fixture.Context(condition.Item)));
        group.DeleteMember(fixture.Member.HabboId);
        Assert.False(condition.Execute(fixture.Context(condition.Item)));
    }

    private static Group Group(int id, int member) => new(id, "Group", "", "", 1, 99,
        DateTimeOffset.UnixEpoch, 0, 1, 1, 0, false, new GroupMembershipSnapshot([member], [], []));

    private static IDisposable UnavailableGlobalGame()
    {
        var field = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        var game = DispatchProxy.Create<IGame, ThrowingGameProxy>();
        ((ThrowingGameProxy)(object)game).InvokeMethod = method => method.Name switch
        {
            "get_ClientManager" => TestWiredClients.Empty,
            "get_GroupManager" => throw new InvalidOperationException("Global group manager access is forbidden."),
            _ => null
        };
        field.SetValue(null, game);

        return new Restore(() => field.SetValue(null, original));
    }

    public class ThrowingGameProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> InvokeMethod { get; set; } = _ => null;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private sealed class Fixture
    {
        public Room Room { get; }
        public RoomUser Member { get; }
        public RoomUser NonMember { get; }
        public RoomUser Bot { get; }
        private readonly WiredComponent _wired;
        private readonly Item[] _items = new Item[2];

        public Fixture(IGroupManager groups, Group? roomGroup)
        {
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            Room.Id = 1;
            Room.Group = roomGroup!;
            var map = new Gamemap(Room, new RoomModel("wired-group", 0, 0, 0, 0, "00\r00", 0, 0, true),
                TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Room, map);
            typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Room, new RoomItemHandling(Room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
            typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Room, new RoomUserManager(Room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
            _wired = new(Room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty,
                TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance,
                TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, groups, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Room, _wired);
            Member = new(1, 0, 101, Room, Client(1, "Member"), TestChatEmotions.Unused, TestRewardProgress.Unused);
            NonMember = new(2, 0, 102, Room, Client(2, "NonMember"), TestChatEmotions.Unused, TestRewardProgress.Unused);
            Bot = new(0, 0, 103, Room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)
            {
                BotData = (Plus.HabboHotel.Rooms.AI.RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(Plus.HabboHotel.Rooms.AI.RoomBot))
            };
            Bot.BotData.Name = "Bot";
        }

        public IWiredContextualSelector Selector(int[] parameters)
        {
            var item = Item(1, "wf_slc_users_group");
            var box = Assert.IsAssignableFrom<IWiredContextualSelector>(_wired.CreateConfiguredBox(item));
            Assert.True(box.TryValidateConfiguration(new() { IntParams = [.. parameters] }, out var valid, out var error), error);
            box.ApplyConfiguration(valid);
            Assert.True(_wired.AddBox(box));
            _items[0] = item;

            return box;
        }

        public IWiredContextualAddon Addon()
        {
            var item = Item(2, "wf_xtra_text_output_username");
            var box = Assert.IsAssignableFrom<IWiredContextualAddon>(_wired.CreateConfiguredBox(item));
            Assert.True(box.TryValidateConfiguration(new() { IntParams = [2, 0], Text = "users\t," },
                out var valid, out var error), error);
            box.ApplyConfiguration(valid);
            Assert.True(_wired.AddBox(box));
            _items[1] = item;

            return box;
        }

        public WiredModernCondition Condition(int[] parameters)
        {
            var item = Item(3, "wf_cnd_actor_in_group");
            var box = Assert.IsType<WiredModernCondition>(_wired.CreateConfiguredBox(item));
            Assert.True(box.TryValidateConfiguration(new() { IntParams = [.. parameters] }, out var valid, out var error), error);
            box.ApplyConfiguration(valid);

            return box;
        }

        public WiredRuntimeContext Context(params Item[] items)
        {
            var context = new WiredRuntimeContext(Room, new(WiredEventKind.Enter) { Actor = Member },
                new(() => items, () => new[] { Member, NonMember, Bot }), new Operations());
            context.Triggering.UserIds.Add(Member.VirtualId);

            return context;
        }

        private static GameClient Client(int id, string name)
        {
            var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
            var habbo = (Habbo)RuntimeHelpers.GetUninitializedObject(typeof(Habbo));
            habbo.Id = id;
            habbo.Username = name;
            habbo.Client = client;
            client.SetHabbo(habbo);

            return client;
        }

        private static Item Item(uint id, string name) => new()
        {
            Id = id,
            Definition = new()
            {
                Id = id,
                Type = ItemType.Floor,
                ItemName = name,
                InteractionName = name,
                PublicName = name,
                Width = 1,
                Length = 1,
                AdjustableHeights = [],
                VendingIds = []
            }
        };
    }

    private sealed class Operations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) =>
            throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection,
            bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
