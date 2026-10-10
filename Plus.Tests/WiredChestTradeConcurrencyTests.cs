using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using Dapper;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Chests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Trading;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class WiredChestTradeConcurrencyTests
{
    [WiredChestDatabaseTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ActualTradeAndChestSerializeOwnershipAndLiveWallet(bool tradeFirst, bool walletChanges)
    {
        using var pair = new Pair(walletChanges);
        var errors = new ConcurrentQueue<Exception>();
        using var winnerStarted = new ManualResetEventSlim();
        using var loserStarted = new ManualResetEventSlim();
        WiredChestTransferResult? result = null;
        Action chest = () => result = pair.World.Module.Move(pair.World.Client, pair.Request);
        Action trade = pair.Trade.Finish;
        Thread Worker(Action action, ManualResetEventSlim started) => new(() =>
        {
            started.Set();

            try {
                action();
            }
            catch (Exception error) {
                errors.Enqueue(error);
            }
        })
        { IsBackground = true };

        var winner = Worker(tradeFirst ? trade : chest, winnerStarted);
        var loser = Worker(tradeFirst ? chest : trade, loserStarted);
        var loserRunning = false;
        var winnerJoined = false;
        var loserJoined = false;
        Monitor.Enter(pair.World.Habbo.WalletSync);

        try {
            winner.Start();
            Assert.True(winnerStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => (winner.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            var acquired = Monitor.TryEnter(pair.World.Habbo.InventoryMutationSync);

            if (acquired) {
                Monitor.Exit(pair.World.Habbo.InventoryMutationSync);
            }

            // Do not start the competitor if inventory-before-wallet is violated: release/join instead of creating a deadlock.
            Assert.False(acquired);
            loser.Start();
            loserRunning = true;
            Assert.True(loserStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => (loser.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            Assert.Equal(0, pair.TradeDatabase.Calls);
            Assert.Equal(0, pair.ChestDatabase.Calls);
        }
        finally {
            Monitor.Exit(pair.World.Habbo.WalletSync);
            winnerJoined = winner.Join(TimeSpan.FromSeconds(60));
            loserJoined = !loserRunning || loser.Join(TimeSpan.FromSeconds(60));
        }

        Assert.True(winnerJoined);
        Assert.True(loserJoined);
        Assert.Empty(errors);
        Assert.NotNull(result);
        Assert.False(pair.World.Actor.IsTrading);
        Assert.False(pair.BobActor.IsTrading);
        Assert.False(pair.World.Room.GetTrading().TryGetTrade(pair.Trade.Id, out _));

        if (walletChanges) {
            Assert.True(result.Succeeded);
            Assert.Equal(108, pair.World.Habbo.Credits);
            Assert.Equal(100, pair.Bob.Credits);
            Assert.Equal(7, pair.Db.Connection.QuerySingle<int>("SELECT coins FROM wired_chests WHERE item_id=101"));
            Assert.Equal(108, pair.Db.Connection.QuerySingle<int>("SELECT credits FROM users WHERE id=1"));
            Assert.Equal(100, pair.Db.Connection.QuerySingle<int>("SELECT credits FROM users WHERE id=2"));
            Assert.Equal(0, pair.Db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE id=205"));
            Assert.Null(pair.World.Habbo.Inventory!.Furniture.GetItem(205));
            Assert.Null(pair.Bob.Inventory!.Furniture.GetItem(205));
            Assert.Equal(2, pair.Db.Connection.QuerySingle<int>("SELECT user_id FROM items WHERE id=201"));
            Assert.Null(pair.World.Habbo.Inventory.Furniture.GetItem(201));
            Assert.Equal((uint)2, pair.Bob.Inventory.Furniture.GetItem(201)!.OwnerId);
            Assert.Equal(1, pair.LogCount);
            Assert.Equal(1, pair.LedgerCount);
            Assert.Equal(215, pair.Db.Connection.QuerySingle<int>("SELECT SUM(credits)+(SELECT SUM(coins) FROM wired_chests) FROM users"));
            Assert.True(Assert.Single(pair.TradeDatabase.InitialLockObservations));
            Assert.True(Assert.Single(pair.ChestDatabase.InitialLockObservations));
            Assert.Single(pair.AlicePackets, packet => packet.Header == ServerPacketHeader.TradingFinishComposer);
        }
        else if (tradeFirst) {
            Assert.Equal(WiredChestFailure.Invalid, result.Failure);
            Assert.Equal(2, pair.Db.Connection.QuerySingle<int>("SELECT user_id FROM items WHERE id=201"));
            Assert.Null(pair.World.Habbo.Inventory!.Furniture.GetItem(201));
            Assert.Equal((uint)2, pair.Bob.Inventory!.Furniture.GetItem(201)!.OwnerId);
            Assert.Equal(1, pair.LogCount);
            Assert.Equal(0, pair.LedgerCount);
            Assert.True(Assert.Single(pair.TradeDatabase.InitialLockObservations));
            Assert.Empty(pair.ChestDatabase.InitialLockObservations);
        }
        else {
            Assert.True(result.Succeeded);
            Assert.Equal((0, 100), pair.Db.Connection.QuerySingle<(int, int)>("SELECT user_id,chest_item_id FROM items WHERE id=201"));
            Assert.Null(pair.World.Habbo.Inventory!.Furniture.GetItem(201));
            Assert.Null(pair.Bob.Inventory!.Furniture.GetItem(201));
            Assert.Equal(new uint[] { 201 }, pair.Store.Load(pair.World.Chest)!.Items.Select(item => item.Id));
            Assert.Equal(0, pair.LogCount);
            Assert.Equal(1, pair.LedgerCount);
            Assert.Empty(pair.TradeDatabase.InitialLockObservations);
            Assert.True(Assert.Single(pair.ChestDatabase.InitialLockObservations));
            Assert.DoesNotContain(pair.AlicePackets, packet => packet.Header == ServerPacketHeader.TradingFinishComposer);
        }

        Assert.Equal(1, pair.Db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE id=201"));

        if (!walletChanges) {
            Assert.Equal(100, pair.World.Habbo.Credits);
            Assert.Equal(100, pair.Bob.Credits);
            Assert.Equal(200, pair.Db.Connection.QuerySingle<int>("SELECT SUM(credits) FROM users"));
        }
    }

    private sealed class ObservedDatabase(IDatabase inner, Func<bool> locksHeld) : IDatabase
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ConcurrentQueue<bool> InitialLockObservations { get; } = new();
        public bool IsConnected() => inner.IsConnected();
        public IDbConnection Connection()
        {
            if (Interlocked.Increment(ref _calls) == 1) {
                InitialLockObservations.Enqueue(locksHeld());
            }

            return inner.Connection();
        }
    }

    private sealed class Pair : IDisposable
    {
        public WiredChestDatabaseTests.Fixture Db { get; } = new();
        public WiredChestProtocolTests.World World { get; }
        public Habbo Bob { get; }
        public RoomUser BobActor { get; }
        public Trade Trade { get; }
        public DatabaseWiredChestStore Store { get; }
        public ObservedDatabase TradeDatabase { get; }
        public ObservedDatabase ChestDatabase { get; }
        public WiredChestTransfer Request { get; }
        public ConcurrentQueue<(uint Header, byte[] Body)> AlicePackets { get; } = new();
        public ConcurrentQueue<(uint Header, byte[] Body)> BobPackets { get; } = new();
        public int LogCount => Db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM logs_client_trade");
        public int LedgerCount => Db.Connection.QuerySingle<int>("SELECT COUNT(*) FROM wired_chest_transactions");

        public Pair(bool walletChanges)
        {
            World = new(Db.Database);
            Db.Connection.Execute("CREATE TABLE logs_client_trade(id INT AUTO_INCREMENT PRIMARY KEY,`1id` INT,`2id` INT,`1items` TEXT,`2items` TEXT,`timestamp` DATETIME(6) NULL) ENGINE=InnoDB");
            var normal = new ItemDefinition { Id = 1, SpriteId = 44, Type = ItemType.Floor, AllowTrade = true };
            var voucher = new ItemDefinition { Id = 2, SpriteId = 55, Type = ItemType.Floor, AllowTrade = true, InteractionType = InteractionType.Exchange, BehaviourData = 5 };
            var definitions = new TestWiredDefinitions(() => new() { [1] = normal, [2] = voucher });
            var payment = World.Habbo.Inventory!.Furniture.GetItem(201)!;
            payment.Definition = normal;
            Bob = new() { Id = 2, Credits = 100, CurrentRoom = World.Room, Inventory = new() };
            var (bobClient, _) = HabbiconTestSupport.Client(Bob);
            Bob.Client = bobClient;
            Capture(World.Client, AlicePackets);
            Capture(bobClient, BobPackets);
            BobActor = new(2, 42, 8, World.Room, bobClient, TestChatEmotions.Unused, TestRewardProgress.Unused) { UserId = 2 };
            var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World.Room.GetRoomUserManager())!;
            Assert.True(users.TryAdd(8, BobActor));
            var chest = Db.Chest(walletChanges ? 101u : 100u, 1, walletChanges ? WiredChestKind.Coins : WiredChestKind.Furni);
            Db.Enable(chest.Id);
            World.Chest = chest;
            var items = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World.Room.GetRoomItemHandler())!;
            items.Clear();
            Assert.True(items.TryAdd(chest.Id, chest));
            TradeDatabase = new(Db.Database, () => Monitor.IsEntered(World.Habbo.InventoryMutationSync) && Monitor.IsEntered(Bob.InventoryMutationSync)
                && Monitor.IsEntered(World.Habbo.WalletSync) && Monitor.IsEntered(Bob.WalletSync));
            ChestDatabase = new(Db.Database, () => Monitor.IsEntered(World.Habbo.InventoryMutationSync) && Monitor.IsEntered(World.Habbo.WalletSync));
            Store = new(ChestDatabase, definitions);
            World.Module = new(World.Room, Store, World.Clock, World.Events.Add);
            typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_chests", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(World.Room.GetWired(), World.Module);
            var settings = new TestRoomSettings(new() { ["trading.auto_exchange_redeemables"] = walletChanges ? "1" : "0" });
            new RoomTradingComponent(TradeDatabase, World.Clock, settings).Initiate(World.Room);
            Assert.True(World.Room.GetTrading().StartTrade(World.Actor, BobActor, out var trade));
            Trade = trade;
            Trade.Users[0].OfferedItems.Add(payment.Id, payment);

            if (walletChanges) {
                Db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(205,2,0,2,''); UPDATE wired_chests SET coins=10 WHERE item_id=101");
                var redeemed = new InventoryItem { Id = 205, OwnerId = 2, Definition = voucher };
                Assert.True(Bob.Inventory!.Furniture.AddItem(redeemed));
                Trade.Users[1].OfferedItems.Add(redeemed.Id, redeemed);
            }

            Request = new() { UserId = 1, RoomId = 42, ChestIds = [chest.Id], PaymentIds = walletChanges ? [] : [201], Reward = walletChanges ? [new(3)] : [], Wired = true };
        }

        private static void Capture(FlashGameClient client, ConcurrentQueue<(uint Header, byte[] Body)> packets)
        {
            client.SendCallback = args =>
            {
                var bytes = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();
                packets.Enqueue((BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));

                return true;
            };
        }

        public void Dispose() => Db.Dispose();
    }
}
