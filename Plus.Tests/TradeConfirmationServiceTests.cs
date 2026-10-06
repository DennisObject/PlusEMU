using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Inventory.Trading;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.HabboHotel;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Rooms.Trading;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Trade game fixture")]
public sealed class TradeConfirmationServiceTests
{
    [Fact]
    public async Task TradingHandlersOnlyDelegateWithoutReadingPackets()
    {
        var calls = new List<string>();
        var trades = new RecordingTrades(calls);
        var habbo = new Habbo { Id = 1, Username = "Alice" };
        var (session, _) = HabbiconTestSupport.Client(habbo);

        await new TradingAcceptEvent(trades).Parse(session, HabbiconTestSupport.Incoming());
        await new TradingConfirmEvent(trades).Parse(session, HabbiconTestSupport.Incoming());
        await new TradingModifyEvent(trades).Parse(session, HabbiconTestSupport.Incoming());
        await new TradingCancelEvent(trades).Parse(session, HabbiconTestSupport.Incoming());
        await new TradingCancelConfirmEvent(trades).Parse(session, HabbiconTestSupport.Incoming());

        Assert.Equal(new[] { "Accept", "Confirm", "Modify", "Cancel", "CancelConfirmation" }, calls);
    }

    [Fact]
    public void AcceptThenConfirmRunsTwoPhasesAndFinishesOnce()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        var trade = f.Start(alice, bob);
        var trades = f.Trades;

        trades.Accept(alice.Session);
        Assert.Equal(new uint[] { ServerPacketHeader.TradingAcceptComposer }, alice.Sent);
        Assert.Equal(new uint[] { ServerPacketHeader.TradingAcceptComposer }, bob.Sent);
        Assert.True(trade.Users[0].HasAccepted);

        trades.Accept(bob.Session);
        Assert.False(trade.CanChange);
        Assert.False(trade.Users[0].HasAccepted);
        Assert.False(trade.Users[1].HasAccepted);
        Assert.Equal(new uint[] { ServerPacketHeader.TradingAcceptComposer, ServerPacketHeader.TradingAcceptComposer, ServerPacketHeader.TradingCompleteComposer }, alice.Sent);

        trades.Confirm(alice.Session);
        Assert.True(trade.Users[0].HasAccepted);
        var confirmation = new FlashIncomingPacket { Buffer = alice.Packets.Last().Payload };
        Assert.Equal((1, 1), (confirmation.ReadInt(), confirmation.ReadInt()));
        Assert.False(confirmation.HasDataRemaining());
        Assert.True(f.Trading.TryGetTrade(trade.Id, out _));

        trades.Confirm(bob.Session);
        Assert.False(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.Equal(new uint[] { ServerPacketHeader.TradingAcceptComposer, ServerPacketHeader.TradingAcceptComposer, ServerPacketHeader.TradingCompleteComposer, ServerPacketHeader.TradingAcceptComposer, ServerPacketHeader.TradingAcceptComposer, ServerPacketHeader.TradingFinishComposer }, alice.Sent);
        Assert.Equal(alice.Sent, bob.Sent);
        Assert.Equal(new[] { (1, 2, "", "") }, f.Store.Logged);
        Assert.False(alice.RoomUser.IsTrading);
        Assert.False(bob.RoomUser.IsTrading);
        Assert.False(alice.RoomUser.HasStatus("trd"));
    }

    [Fact]
    public void ModifyAndConfirmAreGatedUntilBothAccept()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        var trade = f.Start(alice, bob);

        f.Trades.Confirm(alice.Session);
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
        Assert.False(trade.Users[0].HasAccepted);

        f.Trades.Modify(alice.Session); // Editing is open, so modify is sent to both traders.
        Assert.Equal(new uint[] { ServerPacketHeader.TradingAcceptComposer }, alice.Sent);
        Assert.Equal(new uint[] { ServerPacketHeader.TradingAcceptComposer }, bob.Sent);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.Accept(alice.Session);
        f.Trades.Accept(bob.Session);
        Assert.False(trade.CanChange);
        f.Trades.Accept(alice.Session);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.Modify(alice.Session); // Once both have accepted, modify is gated off.
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
        Assert.True(trade.Users[0].HasAccepted);
    }

    [Fact]
    public void MissingTradeSendsClosedToActorOnlyExceptSilentCancelConfirmation()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        alice.RoomUser.TradeId = 999;

        f.Trades.Accept(alice.Session);
        f.Trades.Confirm(alice.Session);
        f.Trades.Modify(alice.Session);
        f.Trades.Cancel(alice.Session);
        Assert.Equal(new uint[] { ServerPacketHeader.TradingClosedComposer, ServerPacketHeader.TradingClosedComposer, ServerPacketHeader.TradingClosedComposer, ServerPacketHeader.TradingClosedComposer }, alice.Sent);
        Assert.Empty(bob.Sent);

        alice.Packets.Clear();
        f.Trades.CancelConfirmation(alice.Session);
        Assert.Empty(alice.Sent);
    }

    [Fact]
    public void CancelEndsTheTradeForBothParticipants()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        var trade = f.Start(alice, bob);

        f.Trades.Cancel(bob.Session);
        Assert.False(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.Equal(new uint[] { ServerPacketHeader.TradingClosedComposer }, alice.Sent);
        Assert.Equal(new uint[] { ServerPacketHeader.TradingClosedComposer }, bob.Sent);
        Assert.False(alice.RoomUser.IsTrading);
        Assert.False(bob.RoomUser.IsTrading);
        Assert.Empty(f.Store.Logged);
    }

    [Fact]
    public void CancelConfirmationEndsTheTradeWithoutClosedWhenTradeIsMissing()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        var trade = f.Start(alice, bob);

        f.Trades.CancelConfirmation(alice.Session);
        Assert.False(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.Equal(new uint[] { ServerPacketHeader.TradingClosedComposer }, alice.Sent);
        Assert.Equal(new uint[] { ServerPacketHeader.TradingClosedComposer }, bob.Sent);

        alice.Packets.Clear();
        bob.Packets.Clear();
        f.Trades.CancelConfirmation(alice.Session);
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
    }

    [Fact]
    public void StaleNonParticipantCannotActForTheOtherTrader()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        var stranger = f.Join(3, 9);
        var trade = f.Start(alice, bob);
        stranger.RoomUser.TradeId = trade.Id;
        stranger.RoomUser.IsTrading = true;

        f.Trades.Accept(stranger.Session);
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
        Assert.False(trade.Users[0].HasAccepted);
        Assert.False(trade.Users[1].HasAccepted);

        f.Trades.Accept(alice.Session);
        f.Trades.Accept(bob.Session);
        Assert.False(trade.CanChange);
        f.Trades.Accept(bob.Session);
        Assert.True(trade.Users[1].HasAccepted);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.Modify(stranger.Session);
        f.Trades.Confirm(stranger.Session);
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
        Assert.True(trade.Users[1].HasAccepted);
        Assert.False(trade.Users[0].HasAccepted);
        Assert.True(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.True(alice.RoomUser.HasStatus("trd"));
        Assert.True(bob.RoomUser.HasStatus("trd"));
    }

    [Fact]
    public void StaleNonParticipantCannotCancelOrLeaveTheTradeChanged()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        var stranger = f.Join(3, 9);
        var trade = f.Start(alice, bob);
        stranger.RoomUser.TradeId = trade.Id;
        stranger.RoomUser.IsTrading = true;
        f.Trades.Accept(alice.Session);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.Cancel(stranger.Session);
        Assert.True(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
        Assert.Empty(stranger.Sent);
        Assert.True(trade.Users[0].HasAccepted);
        Assert.True(alice.RoomUser.IsTrading);
        Assert.True(bob.RoomUser.IsTrading);
        Assert.Equal(trade.Id, alice.RoomUser.TradeId);
        Assert.Equal(trade.Id, bob.RoomUser.TradeId);
        Assert.True(alice.RoomUser.HasStatus("trd"));
        Assert.True(bob.RoomUser.HasStatus("trd"));

        f.Trades.CancelConfirmation(stranger.Session);
        Assert.True(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
        Assert.Empty(stranger.Sent);
        Assert.True(trade.Users[0].HasAccepted);
        Assert.True(alice.RoomUser.IsTrading);
        Assert.True(bob.RoomUser.IsTrading);
        Assert.True(alice.RoomUser.HasStatus("trd"));
        Assert.True(bob.RoomUser.HasStatus("trd"));
    }

    [Fact]
    public void PersistedCompletionFailureKeepsTheTradeRegisteredAndUnfinished()
    {
        using var f = new TradeFixture();
        var (alice, bob) = (f.Join(1, 7), f.Join(2, 8));
        var trade = f.Start(alice, bob);
        f.Store.LogFailure = new InvalidOperationException("Injected log failure");
        f.Trades.Accept(alice.Session);
        f.Trades.Accept(bob.Session);
        f.Trades.Confirm(alice.Session);

        Assert.Throws<InvalidOperationException>(() => f.Trades.Confirm(bob.Session));

        Assert.True(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.DoesNotContain(ServerPacketHeader.TradingFinishComposer, alice.Sent);
        Assert.DoesNotContain(ServerPacketHeader.TradingFinishComposer, bob.Sent);
        Assert.False(alice.RoomUser.IsTrading);
        Assert.False(bob.RoomUser.IsTrading);
        Assert.Equal(0, alice.RoomUser.TradeId);
        Assert.Equal(0, bob.RoomUser.TradeId);
        Assert.False(alice.RoomUser.HasStatus("trd"));
    }

    internal sealed record Actor(Habbo Habbo, GameClient Session, List<(uint Header, byte[] Payload)> Packets, RoomUser RoomUser)
    {
        public List<uint> Sent => Packets.Select(packet => packet.Header).ToList();
    }

    private sealed class RecordingTrades(List<string> calls) : ITradeRequestService
    {
        public void Start(GameClient session, int virtualUserId) => calls.Add("Start");
        public void Accept(GameClient session) => calls.Add("Accept");
        public void Confirm(GameClient session) => calls.Add("Confirm");
        public void Modify(GameClient session) => calls.Add("Modify");
        public void Cancel(GameClient session) => calls.Add("Cancel");
        public void CancelConfirmation(GameClient session) => calls.Add("CancelConfirmation");
        public void OfferItem(GameClient session, uint itemId) => calls.Add("OfferItem");
        public void OfferItems(GameClient session, int amount, uint itemId) => calls.Add("OfferItems");
        public void RemoveItem(GameClient session, uint itemId) => calls.Add("RemoveItem");
    }

    internal sealed class RecordingTradeStore : ITradeStore
    {
        public List<(int, int, string, string)> Logged { get; } = [];
        public Exception? LogFailure
        {
            get; set;
        }
        public void DeleteItem(uint itemId)
        {
        }
        public void TransferItem(uint itemId, int userId)
        {
        }
        public void Log(int firstUserId, int secondUserId, string firstItems, string secondItems)
        {
            if (LogFailure != null)
            {
                throw LogFailure;
            }

            Logged.Add((firstUserId, secondUserId, firstItems, secondItems));
        }
    }

    private sealed class NoTradingLocks : ITradingLockService
    {
        public DateTimeOffset Set(int userId, TimeSpan duration) => throw new NotSupportedException();
        public void Clear(int userId) => throw new NotSupportedException();
        public bool IsLocked(Habbo habbo) => false;
    }

    internal sealed class TradeFixture : IDisposable
    {
        private static readonly FieldInfo GameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _originalGame;
        private readonly GameClientManager _clients = new(null!, null!);
        public readonly Room Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        public readonly RecordingTradeStore Store = new();
        public readonly TradeRequestService Trades;
        public readonly TradingComponent Trading;

        public TradeFixture(ISettingsManager? settings = null)
        {
            typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Room, new RoomUserManager(Room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
            TestRoomUserSnapshots.Install(Room);
            Trading = new TradingComponent(Room, Store, settings ?? TestRoomSettings.Empty);
            Room.SetTrading(Trading);
            Trades = new TradeRequestService(new NoTradingLocks());
            _originalGame = GameField.GetValue(null);
            var game = DispatchProxy.Create<IGame, GameProxy>();
            ((GameProxy)(object)game).Handler = (method, _) => method.Name == "get_ClientManager" ? _clients : throw new NotSupportedException(method.Name);
            GameField.SetValue(null, game);
        }

        public Actor Join(int habboId, int virtualId)
        {
            var habbo = new Habbo { Id = habboId, Username = $"user{habboId}", CurrentRoom = Room, Inventory = new() { Furniture = new([], []), Badges = new(new()) } };
            var packets = new List<(uint Header, byte[] Payload)>();
            // Production revisions exclude zero IDs from their outgoing map.
            var session = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new Revision
                {
                    InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.FieldType == typeof(uint)).Select(field => (uint)field.GetValue(null)!).Where(id => id > 0).Distinct().ToDictionary(id => id, id => id)
                },
                SendCallback = args =>
                {
                    var bytes = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();
                    packets.Add((BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));

                    return true;
                }
            };
            session.SetHabbo(habbo);
            habbo.Client = session;
            _clients.RegisterClient(session, habboId, habbo.Username);
            var roomUser = new RoomUser(habboId, 42, virtualId, Room, session, TestChatEmotions.Unused, TestRewardProgress.Unused) { UserId = habboId };
            var users = (System.Collections.Concurrent.ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Room.GetRoomUserManager())!;
            users[virtualId] = roomUser;

            return new(habbo, session, packets, roomUser);
        }

        public Trade Start(Actor first, Actor second)
        {
            Assert.True(Trading.StartTrade(first.RoomUser, second.RoomUser, out var trade));
            first.RoomUser.SetStatus("trd");
            second.RoomUser.SetStatus("trd");

            return trade;
        }

        public void Dispose() => GameField.SetValue(null, _originalGame);

        public class GameProxy : DispatchProxy
        {
            public Func<System.Reflection.MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();
            protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
        }
    }
}
