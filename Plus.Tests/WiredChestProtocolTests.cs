using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dapper;
using Plus.Communication.Flash;
using Plus.Database;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Incoming.WiredChests;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Chests;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public sealed class WiredChestProtocolTests
{
    [Fact]
    public void CustomOctanePacketsMatchTheRendererFieldOrderAndBothDirectionsAreRegistered()
    {
        var contract = new WiredChestContract { Kind = WiredContractKind.Trade, ReceiveText = "Pay", Payment = [[new(5)]], Reward = [new(2, new(false, 44))] };
        var open = new RecordingPacket();
        new WiredChestTradeOpenComposer(contract, WiredChestKind.Furni, 1, 3, 30, false).Compose(open);
        Assert.Equal(new object[] { 1, "Pay", "generic", true, false, 30, 1, 1, 0, -1, false, 0, 15,
            true, 1, 1, 0, false, 44, 6 }, open.Writes);
        var items = new RecordingPacket();
        new WiredChestTradeItemsComposer(1, [], contract, 0, 1).Compose(items);
        Assert.Equal(new object[] { 1, false, 0, 0, 1, 44, 2, 0, 1, 0, -1, false, 0, 5 }, items.Writes);
        var coins = new RecordingPacket();
        new WiredChestContentsComposer(new(100, 1, 42, WiredChestKind.Coins, 20, 0, new() { Capacity = 5000 }, [])).Compose(coins);
        Assert.Equal(new object[] { 100u, "", "", 5000, 20, false, false, 0, false, false, false, false, false,
            0, 1, -1, 20, 0, 0, true, 5000, true, false, 0, false, false, 0, 1, false, false, false }, coins.Writes);

        foreach (var revision in new[] { "1.6.6.json", "3.6.0.json", "OCTANE-3-6-0-FLOOR-20260909.json", "example.json" }) {
            using var json = JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Resources/Revisions/" + revision)));
            Assert.Equal(9327, json.RootElement.GetProperty("IncomingHeaders").GetProperty("ChestOpenEvent").GetInt32());
            Assert.Equal(9331, json.RootElement.GetProperty("OutgoingHeaders").GetProperty("WiredChestTradeOpenComposer").GetInt32());
        }

        Assert.Equal(9331u, ServerPacketHeader.WiredChestTradeOpenComposer);
    }

    [Fact]
    public void ChestAndTransactionBuiltinsUseActualNumericValuesAndPresenceWithCapturedIdentity()
    {
        var world = new World();
        world.Store.Settings = world.Store.Settings with { AutoLock = false };
        var context = new WiredRuntimeContext(world.Room, new(WiredEventKind.Use) { Actor = world.Actor },
            new(() => [world.Chest], () => [world.Actor]), new Operations());
        var user = WiredVariableRuntimeFrames.UserHolder(world.Actor);
        var furni = WiredVariableRuntimeFrames.FurniHolder(world.Chest);
        long? Read(string key) => WiredChestVariables.Read(world.Module, context, key.StartsWith('~') ? furni : user, key);
        Assert.Equal(1000, Read("~chest.capacity"));
        Assert.Equal(1000, WiredChestVariables.Read(world.Module, null, furni, "~chest.capacity", world.Room));
        Assert.Null(Read("~chest.locked"));
        Assert.Null(Read("~chest.is_auto_lock"));
        Assert.False(WiredChestVariables.HasNumericValue(WiredVariableTarget.Furni, "~chest.locked"));
        Assert.False(WiredChestVariables.HasNumericValue(WiredVariableTarget.User, "@transaction.can_accept"));
        Assert.True(WiredChestVariables.HasNumericValue(WiredVariableTarget.Furni, "~chest.capacity"));
        world.Store.Settings = world.Store.Settings with { AutoLock = true, Locked = true, EveryoneCanOpen = true, EveryoneCanDonate = true };

        foreach (var key in new[] { "~chest.is_auto_lock", "~chest.locked", "~chest.is_open", "~chest.is_donatable" }) {
            Assert.Equal(1, Read(key));
        }

        world.Store.Settings = world.Store.Settings with { Locked = false };
        Assert.Null(Read("@transaction.in_trade"));
        Assert.Null(Read("@transaction.state"));
        Assert.True(world.Module.Start(world.Client, 80, [world.Chest], world.Contract, timeout: 30));
        Assert.Equal(1, Read("@transaction.in_trade"));
        Assert.Equal(1, WiredChestVariables.Read(world.Module, null, user, "@transaction.in_trade", world.Room));
        Assert.Equal(80, Read("@transaction.contract_id"));
        Assert.Equal(1, Read("@transaction.state"));
        Assert.Equal(0, Read("@transaction.current_multiplier"));
        Assert.Null(Read("@transaction.can_accept"));
        Assert.Null(Read("@transaction.start_time")); // Its official unit is not established by the pinned source.
        world.Module.Offer(world.Client, [201], true);
        Assert.Equal(1, Read("@transaction.current_multiplier"));
        Assert.Equal(1, Read("@transaction.can_accept"));
        world.Module.Confirm(world.Client, false);
        Assert.Equal(2, Read("@transaction.state"));
        Assert.Null(Read("@transaction.can_accept"));
        world.Module.Confirm(world.Client, true);
        Assert.Null(Read("@transaction.in_trade"));
        Assert.Null(Read("@transaction.current_multiplier"));
        Assert.True(world.Module.Start(world.Client, 81, [world.Chest], world.Contract, timeout: 30));
        world.Clock.Now += TimeSpan.FromSeconds(30);
        Assert.Null(Read("@transaction.can_accept"));
        world.Module.Poll();
        Assert.Null(Read("@transaction.state"));
        ((ConcurrentDictionary<uint, Item>)Get(world.Room.GetRoomItemHandler(), "_floorItems"))[100] = new() { Id = 100 };
        Assert.Null(Read("~chest.capacity"));
    }

    [Fact]
    public void ContractConsumesOnlyRequiredOwnedItemsAndGroupsDuplicateNodes()
    {
        var chair = new ItemDefinition { SpriteId = 44, Type = ItemType.Floor, AllowTrade = true };
        InventoryItem Item(uint id) => new() { Id = id, Definition = chair };
        var offered = new[] { Item(1), Item(2), Item(3), Item(4), Item(5) };
        var contract = new WiredChestContract { Kind = WiredContractKind.Payment, PaymentMode = 1, Payment = [[new(1, new(false, 44)), new(1, new(false, 44))]] };
        Assert.Equal(2, WiredChestContracts.Times(contract, offered, 2, 500));
        Assert.Equal(new uint[] { 1, 2 }, WiredChestContracts.Payment(contract, offered, 0, 1));
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, WiredChestContracts.Payment(contract, offered, 2, 500));
        Assert.Empty(WiredChestContracts.Payment(contract, offered, 1, 3));
        var vouchers = new[] { new InventoryItem { Id = 6, Definition = new() { InteractionType = InteractionType.Exchange, BehaviourData = 20 } } };
        contract = contract with { Payment = [[new(5)]] };
        Assert.Equal(new uint[] { 6 }, WiredChestContracts.Payment(contract, vouchers, 0, 1));
    }

    [Fact]
    public void SessionRequiresTwoConfirmationsCommitsOnceAndPublishesActualOutcome()
    {
        var world = new World();
        Assert.True(world.Module.Start(world.Client, 80, [world.Chest], world.Contract));
        Assert.True(world.Actor.IsTrading);
        world.Module.Offer(world.Client, [201], true);
        world.Module.Confirm(world.Client, true);
        Assert.Empty(world.Store.Requests);
        world.Module.Confirm(world.Client, false);
        world.Module.Confirm(world.Client, true);
        world.Module.Confirm(world.Client, true);
        Assert.Single(world.Store.Requests);
        Assert.False(world.Actor.IsTrading);
        Assert.Null(world.Habbo.Inventory.Furniture.GetItem(201));
        Assert.Equal(WiredEventKind.TransactionComplete, Assert.Single(world.Events).Kind);
        Assert.Equal(new WiredChestFigures(1, 1, 0, 0, 0), world.Events[0].Transaction);
        Assert.Contains(world.Packets, packet => packet.Header == 9334);
        Assert.Equal(1, world.Store.Requests[0].Multiplier);
    }

    [Theory]
    [InlineData(WiredChestFailure.UserCancelled)]
    [InlineData(WiredChestFailure.Timeout)]
    [InlineData(WiredChestFailure.TradeCancelled)]
    public void CancelTimeoutAndActorLeavePublishFailureWithoutPayment(WiredChestFailure reason)
    {
        var world = new World();
        Assert.True(world.Module.Start(world.Client, 80, [world.Chest], world.Contract, timeout: 30));
        world.Module.Offer(world.Client, [201], true);

        if (reason == WiredChestFailure.UserCancelled) {
            world.Module.Cancel(world.Client);
        }
        else if (reason == WiredChestFailure.Timeout) {
            world.Clock.Now += TimeSpan.FromSeconds(30);
            world.Module.Poll();
        }
        else {
            world.Module.Leave(world.Actor);
        }

        Assert.Equal((int)reason, Assert.Single(world.Events).Code);
        Assert.Equal(WiredEventKind.TransactionFail, world.Events[0].Kind);
        Assert.NotNull(world.Habbo.Inventory.Furniture.GetItem(201));
        Assert.Empty(world.Store.Requests);
        Assert.False(world.Actor.IsTrading);
    }

    [Fact]
    public void CancellationGraceAllowsReplacementAndOrdinaryTradeCannotBeOverwritten()
    {
        var world = new World();
        world.Actor.IsTrading = true;
        Assert.False(world.Module.Start(world.Client, 80, [world.Chest], world.Contract));
        Assert.Equal((int)WiredChestFailure.AlreadyTrading, Assert.Single(world.Events).Code);
        world.Actor.IsTrading = false;
        world.Events.Clear();
        Assert.True(world.Module.Start(world.Client, 80, [world.Chest], world.Contract));
        Assert.True(world.Module.CancelByWired([world.Actor], [80], false));
        world.Clock.Now += TimeSpan.FromMilliseconds(499);
        world.Module.Poll();
        Assert.True(world.Module.HasPending);
        Assert.True(world.Module.Start(world.Client, 81, [world.Chest], world.Contract));
        Assert.Single(world.Events);
        world.Module.CancelByWired([world.Actor], [], true);
        world.Clock.Now += TimeSpan.FromMilliseconds(500);
        world.Module.Poll();
        Assert.False(world.Module.HasPending);
        Assert.Equal(2, world.Events.Count);
    }

    [WiredChestDatabaseFact]
    public void DynamicRewardReadsTheEventVariableAndCannotUseStalePickedChestIdentity()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var world = new World(fixture.Database);
        world.Store.Kind = WiredChestKind.Coins;
        var descriptor = WiredBoxRegistry.All.Single(box => box.CanonicalName == "wf_act_give_currency");
        var effect = (WiredChestAction)WiredChestBox.Create(world.Room, new() { Id = 9 }, descriptor, world.Module);
        var data = new WiredChestEditorData { Variables = ["internal:@event.transaction_complete.multiplier"], Picks = [[100], []] };
        Assert.True(effect.TryValidateConfiguration(new() { IntParams = [0, 1, 1, 2, 1, 11], SelectedItems = [100], Text = "@chest:" + JsonSerializer.Serialize(data) }, out var configuration, out _));
        effect.ApplyConfiguration(configuration);
        var context = new WiredRuntimeContext(world.Room, new(WiredEventKind.TransactionComplete) { Actor = world.Actor, Transaction = new(7, 0, 0, 0, 0) }, new(() => [world.Chest], () => [world.Actor]), new Operations());
        context.Triggering.UserIds.Add(world.Actor.VirtualId);
        context.VariableFrame = new(42, []) { RuntimeContext = context };
        Assert.True(effect.Execute(context));
        Assert.Equal(7, Assert.Single(world.Store.Requests).Reward[0].Amount);
        Assert.Contains(world.Packets, packet => packet.Header == 9346);
        var holder = new WiredVariableHolder(WiredVariableTarget.Context, 0, 0);
        Assert.Equal(7, WiredChestVariables.Read(world.Module, context, holder, "@event.transaction_complete.multiplier"));
        Assert.Null(WiredChestVariables.Read(world.Module, context, holder, "@event.transaction_failed.reason"));
        ((ConcurrentDictionary<uint, Item>)Get(world.Room.GetRoomItemHandler(), "_floorItems"))[100] = new() { Id = 100 };
        Assert.False(effect.Execute(context));
        Assert.Single(world.Store.Requests);
    }
    [WiredChestDatabaseFact]
    public void WideGiveCurrencyOperandCannotWrapOrPayOutsideSupportedDomain()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(10,1,42,1,''); INSERT INTO wired_item_configurations VALUES(10,'wf_var_context',@config)", new { config = JsonSerializer.Serialize(new WiredConfiguration { Text = "wide_amount", IntParams = [1] }) });
        var world = new World(fixture.Database);
        world.Store.Kind = WiredChestKind.Coins;
        world.Store.Coins = int.MaxValue;
        var second = new Item { Id = 101, OwnerId = 1, RoomId = 42, Definition = world.Chest.Definition };
        ((ConcurrentDictionary<uint, Item>)Get(world.Room.GetRoomItemHandler(), "_floorItems")).TryAdd(101, second);
        var effect = (WiredChestAction)WiredChestBox.Create(world.Room, new() { Id = 9 }, WiredBoxRegistry.All.Single(box => box.CanonicalName == "wf_act_give_currency"), world.Module);
        var data = new WiredChestEditorData { Variables = ["custom:10"], Picks = [[100, 101], []] };
        Assert.True(effect.TryValidateConfiguration(new() { IntParams = [0, 1, 1, 2, 1, 11], SelectedItems = [100, 101], Text = "@chest:" + JsonSerializer.Serialize(data) }, out var configuration, out _));
        effect.ApplyConfiguration(configuration);
        var context = new WiredRuntimeContext(world.Room, new(WiredEventKind.Use) { Actor = world.Actor }, new(() => [world.Chest, second], () => [world.Actor]), new Operations());
        context.Triggering.UserIds.Add(world.Actor.VirtualId);
        context.VariableFrame = new(42, []) { RuntimeContext = context };

        foreach (var amount in new[] { (long)int.MaxValue + 1, 9007199254740993L, long.MaxValue, long.MinValue }) {
            Assert.True(world.Room.GetWired().Variables.Module.CaptureContextValues(new Dictionary<uint, long> { [10] = amount }, context.VariableFrame));
            Assert.False(effect.Execute(context));
        }

        Assert.True(effect.TryValidateConfiguration(configuration with { IntParams = [1, 1, 0, 2, 1, 11] }, out configuration, out _));
        effect.ApplyConfiguration(configuration);
        Assert.False(effect.Execute(context)); // Two full coin chests exceed one supported wallet-domain payout.
        Assert.Empty(world.Store.Requests);
        Assert.Equal(100, world.Habbo.Credits);
        Assert.DoesNotContain(world.Packets, packet => packet.Header == 9346);
    }

    [WiredChestDatabaseFact]
    public void ScannerCapturesCountThenDynamicCustomContractPaysThatCountAndUsesSourceTriggers()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(10,1,42,1,''); INSERT INTO wired_item_configurations VALUES(10,'wf_var_context',@config)", new { config = JsonSerializer.Serialize(new WiredConfiguration { Text = "chest_count", IntParams = [1] }) });
        var world = new World(fixture.Database);
        world.Store.Stock = [new() { Id = 301, Definition = new() { SpriteId = 44, Type = ItemType.Floor } }, new() { Id = 302, Definition = new() { SpriteId = 44, Type = ItemType.Floor } }];
        var template = new Item { Id = 101, OwnerId = 1, RoomId = 42, Definition = new() { SpriteId = 44, Type = ItemType.Floor } };
        ((ConcurrentDictionary<uint, Item>)Get(world.Room.GetRoomItemHandler(), "_floorItems")).TryAdd(101, template);
        var context = new WiredRuntimeContext(world.Room, new(WiredEventKind.Use) { Actor = world.Actor }, new(() => [world.Chest, template], () => [world.Actor]), new Operations());
        context.Triggering.UserIds.Add(world.Actor.VirtualId);
        context.VariableFrame = new(42, []) { RuntimeContext = context };
        WiredChestBox Box(string name, uint id, int[] ints, WiredChestEditorData fields, uint[] picks)
        {
            var box = (WiredChestBox)WiredChestBox.Create(world.Room, new() { Id = id }, WiredBoxRegistry.All.Single(entry => entry.CanonicalName == name), world.Module);
            Assert.True(box.TryValidateConfiguration(new() { IntParams = [.. ints], SelectedItems = [.. picks], Text = "@chest:" + JsonSerializer.Serialize(fields) }, out var config, out _));
            box.ApplyConfiguration(config);

            return box;
        }
        var scanner = Box("wf_xtra_scan_chest_furni_by_type", 90, [0], new() { Variables = ["custom:10"], Picks = [[101], [100]] }, [100, 101]);
        Assert.True(scanner.Execute(context));
        Assert.Equal(2, world.Room.GetWired().Variables.Module.Read(new(WiredVariableTarget.Context, "custom:10"), new(WiredVariableTarget.Context, 0, 0), context.VariableFrame)!.Value);
        scanner = Box("wf_xtra_scan_chest_furni_by_type", 90, [1], new() { Variables = ["custom:10"], Picks = [[101], [100]] }, [100, 101]);
        Assert.True(scanner.Execute(context));
        Assert.Equal(1, world.Room.GetWired().Variables.Module.Read(new(WiredVariableTarget.Context, "custom:10"), new(WiredVariableTarget.Context, 0, 0), context.VariableFrame)!.Value);
        var addon = Box("wf_xtra_custom_contract", 91, [0, 0, 0, 1, 0, 1, 0, 1, 1, 2], new() { Variables = ["", "custom:10"], Picks = [[], [], [], []] }, []);
        Assert.True(addon.Execute(context));
        world.Store.Kind = WiredChestKind.Coins;
        var initiate = Box("wf_act_init_transaction", 92, [0, 1, 0, 0, 0, 300], new() { Picks = [[100], [], []] }, [100]);
        Assert.True(initiate.Execute(context));
        Assert.Equal(1, Assert.Single(world.Store.Requests).Reward[0].Amount);
        var outcome = Assert.Single(world.Events);
        Assert.Equal(WiredEventKind.TransactionComplete, outcome.Kind);
        var trigger = (WiredChestTrigger)Box("wf_trg_transaction_complete", 93, [], new(), []);
        var completed = new WiredRuntimeContext(world.Room, outcome, context.Targets, new Operations());
        Assert.True(trigger.CanTrigger(completed));
        Assert.True(trigger.Execute(completed));
        Assert.False(trigger.Execute(context));
    }

    [Fact]
    public void CommitRefusalPublishesFailureAndKeepsInventoryAndWalletUnchanged()
    {
        var world = new World();
        world.Store.Failure = WiredChestFailure.FundsGone;
        Assert.True(world.Module.Start(world.Client, 80, [world.Chest], world.Contract));
        world.Module.Offer(world.Client, [201], true);
        world.Module.Confirm(world.Client, false);
        world.Module.Confirm(world.Client, true);
        Assert.Equal(100, world.Habbo.Credits);
        Assert.NotNull(world.Habbo.Inventory.Furniture.GetItem(201));
        Assert.Equal(WiredEventKind.TransactionFail, Assert.Single(world.Events).Kind);
        Assert.Equal((int)WiredChestFailure.FundsGone, world.Events[0].Code);
        Assert.Single(world.Packets, packet => packet.Header == 9333);
        Assert.DoesNotContain(world.Packets, packet => packet.Header == 9334);
    }

    [Fact]
    public async Task MalformedTruncatedAndTrailingChestPacketsNeverTransfer()
    {
        var world = new World();

        foreach (var values in new object[][] { [], [100], [100, -1], [100, -1, 10, 999] }) {
            await new ChestDepositCoinsEvent().Parse(world.Room, world.Client, Incoming(values));
        }

        await new WiredChestOfferItemsEvent().Parse(world.Room, world.Client, Incoming(false, 501));
        Assert.Empty(world.Store.Requests);
        Assert.Empty(world.Packets);
    }

    [Fact]
    public void RewardAndSettingsAcknowledgementHaveExplicitAdapterShapes()
    {
        var reward = new RecordingPacket();
        new WiredChestRewardComposer(new(null, 100, [], [], new(1, 0, 0, 0, 7)), "earned", true).Compose(reward);
        Assert.Equal(new object[] { 1, 0, -1, false, 0, 7, "earned", true }, reward.Writes);
        var ack = new RecordingPacket();
        new WiredChestSettingsAckComposer(100, false).Compose(ack);
        Assert.Equal(new object[] { 100u, false }, ack.Writes);
    }

    private sealed class Operations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch; public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Store : IWiredChestStore
    {
        public List<WiredChestTransfer> Requests = [];
        public WiredChestFailure? Failure;
        public WiredChestKind Kind = WiredChestKind.Furni;
        public int Coins = 20;
        public InventoryItem[] Stock = [];
        public WiredChestSettings Settings = new() { Capacity = 1000, Locked = false, WiredEnabled = true, PreviewMode = 5, PreviewAmount = 1 };
        public WiredChestSnapshot? Load(Item item) => WiredChestFurniture.IsChest(item.Definition) ? new(item.Id, 1, 42, Kind, Kind == WiredChestKind.Coins ? Coins : 0, 0, Settings, Stock) : null;
        public bool SaveSettings(Item item, int user, bool modify, bool owner, Func<WiredChestSettings, WiredChestSettings> change) => true;
        public WiredChestContract? LoadContract(Item item) => null;
        public bool SaveContract(Item item, WiredChestContract contract) => true;
        public WiredChestUpgradeResult Upgrade(Item item, int user, int count, int? credits = null, int? diamonds = null) => new(4);
        public WiredChestTransferResult Transfer(WiredChestTransfer request)
        {
            Requests.Add(request);

            if (Failure is { } failure) {
                return WiredChestTransferResult.Refused(failure);
            }

            return new(null, 100, [], request.PaymentIds, new(request.Multiplier, request.PaymentIds.Length, 0, 0, 0));
        }
    }
    internal sealed class World
    {
        public Room Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        public Store Store = new(); public Clock Clock = new(); public List<WiredRuntimeEvent> Events = [];
        public FlashGameClient Client; public Habbo Habbo; public RoomUser Actor; public List<(uint Header, byte[] Payload)> Packets;
        public Item Chest = new() { Id = 100, OwnerId = 1, RoomId = 42, Definition = new() { InteractionType = InteractionType.WiredChestFurni, ItemName = "wf_storage_furni1" } };
        public WiredChestContract Contract = new() { Id = 80, Kind = WiredContractKind.Payment, PaymentMode = 1, Payment = [[new(1, new(false, 44))]] };
        public WiredChestRoom Module;
        public World(IDatabase? database = null)
        {
            Room.Id = 42;
            Room.OwnerId = 1;
            Set(Room, "_roomItemHandling", new RoomItemHandling(Room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
            Set(Room, "_roomUserManager", new RoomUserManager(Room, TestRoomUserStore.Instance, Clock, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
            Set(Room, "_wiredComponent", new WiredComponent(Room, TestLogging.Logger, Clock, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, database ?? TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel));
            ((ConcurrentDictionary<uint, Item>)Get(Room.GetRoomItemHandler(), "_floorItems")).TryAdd(Chest.Id, Chest);
            Habbo = new() { Id = 1, Credits = 100, CurrentRoom = Room, Inventory = new() { Furniture = new([new() { Id = 201, OwnerId = 1, Definition = new() { SpriteId = 44, Type = ItemType.Floor, AllowTrade = true } }], []) } };
            (Client, Packets) = HabbiconTestSupport.Client(Habbo);
            Habbo.Client = Client;
            Actor = new RoomUser(1, 1, 7, Room, Client, TestChatEmotions.Unused, TestRewardProgress.Unused);
            ((ConcurrentDictionary<int, RoomUser>)Get(Room.GetRoomUserManager(), "_users")).TryAdd(7, Actor);
            Module = new(Room, Store, Clock, Events.Add);
            Set(Room.GetWired(), "_chests", Module);
        }
    }
    private static object Get(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string name, object data) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
}
