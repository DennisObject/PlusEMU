using System.Buffers.Binary;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Inventory.Trading;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.Trading;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Trade game fixture")]
public sealed class TradeOfferServiceTests
{
    [Fact]
    public async Task OfferAndRemoveHandlersDecodeFullPrimitivesAndDelegateOnce()
    {
        var calls = new List<string>();
        var trades = new RecordingOffers(calls);
        var session = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        session.SetHabbo(new Habbo { Id = 1, Username = "Alice" });

        await new TradingOfferItemEvent(trades).Parse(session, Packet(uint.MaxValue));
        await new TradingOfferItemsEvent(trades).Parse(session, Packet(-3, 4000000000u));
        await new TradingRemoveItemEvent(trades).Parse(session, Packet(0x80000001u));

        Assert.Equal(new[] { "OfferItem 4294967295", "OfferItems -3 4000000000", "RemoveItem 2147483649" }, calls);

        calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingOfferItemsEvent(trades).Parse(session, Packet(1)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingRemoveItemEvent(trades).Parse(session, Packet()));
        Assert.Empty(calls);
    }

    [Fact]
    public void OfferAddsToTheTradeAndResetsAcceptanceWithAnUpdate()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(100, 11));
        f.Trades.Accept(alice.Session);
        alice.Packets.Clear(); bob.Packets.Clear();

        f.Trades.OfferItem(alice.Session, 100);

        Assert.Equal(new uint[] { 100 }, trade.Users[0].OfferedItems.Keys);
        Assert.False(trade.Users[0].HasAccepted);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, alice.Sent);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, bob.Sent);
    }

    [Fact]
    public void RemoveTakesOnlyAnOfferedItemAndUpdatesBothTraders()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(100, 11), Floor(101, 11));
        f.Trades.OfferItem(alice.Session, 100);
        alice.Packets.Clear(); bob.Packets.Clear();

        f.Trades.RemoveItem(alice.Session, 101); // in inventory, never offered
        f.Trades.RemoveItem(alice.Session, 999); // not in inventory
        Assert.Empty(alice.Sent); Assert.Empty(bob.Sent);

        f.Trades.RemoveItem(alice.Session, 100);
        Assert.Empty(trade.Users[0].OfferedItems);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, alice.Sent);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, bob.Sent);
    }

    [Fact]
    public void ChangeGatesAndDuplicatesStayQuiet()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(100, 11), Floor(101, 11));
        f.Trades.OfferItem(alice.Session, 100);
        f.Trades.OfferItem(alice.Session, 100); // duplicate
        Assert.Single(trade.Users[0].OfferedItems);
        alice.Packets.Clear(); bob.Packets.Clear();

        f.Trades.Accept(alice.Session); f.Trades.Accept(bob.Session);
        Assert.False(trade.CanChange);
        alice.Packets.Clear(); bob.Packets.Clear();
        f.Trades.OfferItem(alice.Session, 101);
        f.Trades.RemoveItem(alice.Session, 100);
        f.Trades.OfferItems(alice.Session, 1, 101);
        Assert.Equal(new uint[] { 100 }, trade.Users[0].OfferedItems.Keys);
        Assert.Empty(alice.Sent); Assert.Empty(bob.Sent);
    }

    [Fact]
    public void MissingTradeSendsClosedOnlyToTheActor()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        Stock(alice, Floor(100, 11));
        alice.RoomUser.IsTrading = true;
        alice.RoomUser.TradeId = 999;

        f.Trades.OfferItem(alice.Session, 100);
        f.Trades.OfferItems(alice.Session, 1, 100);
        f.Trades.RemoveItem(alice.Session, 100);

        Assert.Equal(new[] { ServerPacketHeader.TradingClosedComposer, ServerPacketHeader.TradingClosedComposer, ServerPacketHeader.TradingClosedComposer }, alice.Sent);
        Assert.Empty(bob.Sent);
    }

    [Fact]
    public void SingleOfferStopsAtTheLimitAndTheLtdBoundary()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Enumerable.Range(0, 10).Select(i => Floor((uint)(1000 + i), 11, ltd: true)).ToArray());
        for (uint id = 1000; id < 1010; id++) f.Trades.OfferItem(alice.Session, id);
        Assert.Equal(9, trade.Users[0].OfferedItems.Count); // the tenth LTD is refused
        Assert.Equal(10, alice.Sent.Count(header => header == ServerPacketHeader.TradingUpdateComposer)); // but still answered

        var plain = f.Join(3, 9); var second = f.Join(4, 10);
        var secondTrade = f.Start(plain, second);
        Stock(plain, Enumerable.Range(0, 501).Select(i => Floor((uint)(5000 + i), 12)).ToArray());
        for (uint id = 5000; id < 5501; id++) f.Trades.OfferItem(plain.Session, id);
        Assert.Equal(500, secondTrade.Users[0].OfferedItems.Count);
    }

    [Fact]
    public void BatchOffersOnlyTheMatchingDefinitionUpToTheAmountWithoutLtdLimit()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Enumerable.Range(0, 5).Select(i => Floor((uint)(200 + i), 77, ltd: true)).Append(Floor(300, 78)).ToArray());

        f.Trades.OfferItems(alice.Session, 3, 200);

        Assert.Equal(3, trade.Users[0].OfferedItems.Count);
        Assert.All(trade.Users[0].OfferedItems.Values, item => Assert.Equal(77u, item.Definition.Id));
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, alice.Sent);
    }

    [Fact]
    public void BatchOfferIgnoresTheSingleLtdCapForEveryRequestedItem()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Enumerable.Range(0, 10).Select(i => Floor((uint)(600 + i), 90, ltd: true)).ToArray());
        f.Trades.OfferItems(alice.Session, 10, 600);
        Assert.Equal(10, trade.Users[0].OfferedItems.Count);
        Assert.All(trade.Users[0].OfferedItems.Values, item => Assert.True(item.UniqueNumber > 0));

        // A second trade with eleven matching LTD items: the batch path has no 9-LTD cap, so all eleven are offered.
        var carol = f.Join(3, 9); var dave = f.Join(4, 10);
        var secondTrade = f.Start(carol, dave);
        Stock(carol, Enumerable.Range(0, 11).Select(i => Floor((uint)(700 + i), 91, ltd: true)).ToArray());
        f.Trades.OfferItems(carol.Session, 11, 700);
        Assert.Equal(11, secondTrade.Users[0].OfferedItems.Count);
        Assert.All(secondTrade.Users[0].OfferedItems.Values, item => Assert.True(item.UniqueNumber > 0));
    }

    [Fact]
    public void BatchAddsItemsBeforeADuplicateAndStopsSilently()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(200, 77), Floor(201, 77), Floor(202, 77));
        // The batch enumerates the same order that AllItems reports here.
        var ordered = alice.Habbo.Inventory.Furniture.AllItems.Where(x => x.Definition.Id == 77).Select(x => x.Id).ToArray();
        var first = ordered[0]; var second = ordered[1];
        f.Trades.OfferItem(alice.Session, second);
        f.Trades.Accept(alice.Session);
        Assert.True(trade.Users[0].HasAccepted);
        alice.Packets.Clear(); bob.Packets.Clear();

        f.Trades.OfferItems(alice.Session, 2, first);

        Assert.True(trade.Users[0].OfferedItems.ContainsKey(first));
        Assert.True(trade.Users[0].OfferedItems.ContainsKey(second));
        Assert.Equal(2, trade.Users[0].OfferedItems.Count);
        Assert.False(trade.Users[0].HasAccepted);
        Assert.Empty(alice.Sent); Assert.Empty(bob.Sent);
    }

    [Fact]
    public void BatchWithNothingToOfferStillAnswersAndAmountZeroAddsNothing()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(200, 77));

        f.Trades.OfferItems(alice.Session, 0, 200);
        f.Trades.OfferItems(alice.Session, -4, 200);

        Assert.Empty(trade.Users[0].OfferedItems);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer, ServerPacketHeader.TradingUpdateComposer }, alice.Sent);
    }

    [Fact]
    public void BatchDuplicateStopsWithoutAnUpdatePacket()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(200, 77));
        f.Trades.OfferItem(alice.Session, 200);
        alice.Packets.Clear(); bob.Packets.Clear();

        f.Trades.OfferItems(alice.Session, 1, 200);

        Assert.Single(trade.Users[0].OfferedItems);
        Assert.Empty(alice.Sent); Assert.Empty(bob.Sent);
    }

    [Fact]
    public void StaleNonParticipantNeverOperatesOnAnotherTradersSlot()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7); var bob = f.Join(2, 8); var stranger = f.Join(3, 9);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(100, 11));
        Stock(bob, Floor(150, 11));
        f.Trades.OfferItem(alice.Session, 100);
        f.Trades.OfferItem(bob.Session, 150);
        f.Trades.Accept(alice.Session);
        stranger.RoomUser.TradeId = trade.Id;
        stranger.RoomUser.IsTrading = true;
        Stock(stranger, Floor(400, 11), Floor(401, 11), Floor(100, 11), Floor(150, 11));
        f.Trades.OfferItem(stranger.Session, 100);
        alice.Packets.Clear(); bob.Packets.Clear(); stranger.Packets.Clear();

        f.Trades.OfferItem(stranger.Session, 400);
        f.Trades.OfferItems(stranger.Session, 1, 401);
        f.Trades.RemoveItem(stranger.Session, 100);
        f.Trades.RemoveItem(stranger.Session, 150);

        Assert.Equal(new uint[] { 100 }, trade.Users[0].OfferedItems.Keys);
        Assert.Equal(new uint[] { 150 }, trade.Users[1].OfferedItems.Keys);
        Assert.True(trade.Users[0].OfferedItems.ContainsKey(100));
        Assert.True(trade.Users[1].OfferedItems.ContainsKey(150));
        Assert.True(alice.RoomUser.HasStatus("trd")); Assert.True(bob.RoomUser.HasStatus("trd"));
        Assert.True(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.True(trade.Users[0].HasAccepted); // a stale actor cannot reset alice's acceptance
        Assert.False(trade.Users[1].HasAccepted);
        Assert.True(trade.CanChange);
        Assert.Empty(alice.Sent); Assert.Empty(bob.Sent); Assert.Empty(stranger.Sent);
    }

    private static void Stock(TradeConfirmationServiceTests.Actor actor, params InventoryItem[] items) =>
        actor.Habbo.Inventory = new() { Furniture = new(items, []), Badges = new(new()) };

    private static InventoryItem Floor(uint id, int definitionId, bool ltd = false) => new()
    {
        Id = id,
        Definition = new ItemDefinition { Id = (uint)definitionId, Type = ItemType.Floor, SpriteId = 1 },
        UniqueNumber = ltd ? 1u : 0u
    };

    private static IIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = new byte[4];
            switch (value)
            {
                case int number: BinaryPrimitives.WriteInt32BigEndian(bytes, number); stream.Write(bytes); break;
                case uint number: BinaryPrimitives.WriteUInt32BigEndian(bytes, number); stream.Write(bytes); break;
                default: throw new NotSupportedException();
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private sealed class RecordingOffers(List<string> calls) : ITradeRequestService
    {
        public void Start(GameClient session, int virtualUserId) => calls.Add("Start");
        public void Accept(GameClient session) => calls.Add("Accept");
        public void Confirm(GameClient session) => calls.Add("Confirm");
        public void Modify(GameClient session) => calls.Add("Modify");
        public void Cancel(GameClient session) => calls.Add("Cancel");
        public void CancelConfirmation(GameClient session) => calls.Add("CancelConfirmation");
        public void OfferItem(GameClient session, uint itemId) => calls.Add($"OfferItem {itemId}");
        public void OfferItems(GameClient session, int amount, uint itemId) => calls.Add($"OfferItems {amount} {itemId}");
        public void RemoveItem(GameClient session, uint itemId) => calls.Add($"RemoveItem {itemId}");
    }
}
