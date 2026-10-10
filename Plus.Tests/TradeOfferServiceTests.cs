using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Inventory.Trading;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
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
    public async Task OfferItemsHandlerReadsTheSelectedIdsAndRejectsAMalformedList()
    {
        var calls = new List<string>();
        var trades = new RecordingOffers(calls);
        var session = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        session.SetHabbo(new Habbo { Id = 1, Username = "Alice" });
        var offered = Packet(2, 4000000000u, 7u);

        await new TradingOfferItemEvent(trades).Parse(session, Packet(uint.MaxValue));
        await new TradingOfferItemsEvent(trades).Parse(session, offered);
        await new TradingRemoveItemEvent(trades).Parse(session, Packet(0x80000001u));

        Assert.False(offered.HasDataRemaining());
        Assert.Equal(new[] { "OfferItem 4294967295", "OfferItems 4000000000,7", "RemoveItem 2147483649" }, calls);

        calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingOfferItemsEvent(trades).Parse(session, Packet()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingOfferItemsEvent(trades).Parse(session, Packet(0)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingOfferItemsEvent(trades).Parse(session, Packet(-3, 4000000000u)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingOfferItemsEvent(trades).Parse(session, Packet(2, 7u)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingOfferItemsEvent(trades).Parse(session, Packet(1, 7u, 8u)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TradingRemoveItemEvent(trades).Parse(session, Packet()));
        Assert.Empty(calls);
    }

    [Fact]
    public void CurrentHeaderDispatchesTheSelectedIdsAndDropsAMalformedList()
    {
        var air = new RevisionsCache().InternalRevision;
        var offered = Dispatch(OfferFrame(3882, 2, 4000000000u, 7u), air);
        Assert.Equal(new[] { "OfferItems 4000000000,7" }, offered.Calls);
        Assert.Equal(0, offered.Disconnects);
        Assert.Equal(3882u, ClientPacketHeader.TradingOfferItemsEvent);

        var shortList = Dispatch(OfferFrame(3882, 2, 7u), air);
        Assert.Empty(shortList.Calls);
        Assert.Equal(0, shortList.Disconnects);

        var trailing = Dispatch(OfferFrame(3882, 1, 11u, 12u), air);
        Assert.Empty(trailing.Calls);
        Assert.Equal(0, trailing.Disconnects);

        var single = Dispatch(OfferFrame(3882, 1, 11u), air);
        Assert.Equal(new[] { "OfferItems 11" }, single.Calls);
        Assert.Equal(0, single.Disconnects);
    }

    [Fact]
    public void OfferAddsToTheTradeAndResetsAcceptanceWithAnUpdate()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(100, 11));
        f.Trades.Accept(alice.Session);
        alice.Packets.Clear();
        bob.Packets.Clear();

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
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(100, 11), Floor(101, 11));
        f.Trades.OfferItem(alice.Session, 100);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.RemoveItem(alice.Session, 101); // in inventory, never offered
        f.Trades.RemoveItem(alice.Session, 999); // not in inventory
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);

        f.Trades.RemoveItem(alice.Session, 100);
        Assert.Empty(trade.Users[0].OfferedItems);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, alice.Sent);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, bob.Sent);
    }

    [Fact]
    public void ChangeGatesAndDuplicatesStayQuiet()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(100, 11), Floor(101, 11));
        f.Trades.OfferItem(alice.Session, 100);
        f.Trades.OfferItem(alice.Session, 100); // duplicate
        Assert.Single(trade.Users[0].OfferedItems);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.Accept(alice.Session);
        f.Trades.Accept(bob.Session);
        Assert.False(trade.CanChange);
        alice.Packets.Clear();
        bob.Packets.Clear();
        f.Trades.OfferItem(alice.Session, 101);
        f.Trades.RemoveItem(alice.Session, 100);
        f.Trades.OfferItems(alice.Session, new uint[] { 101 });
        Assert.Equal(new uint[] { 100 }, trade.Users[0].OfferedItems.Keys);
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
    }

    [Fact]
    public void MissingTradeSendsClosedOnlyToTheActor()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        Stock(alice, Floor(100, 11));
        alice.RoomUser.IsTrading = true;
        alice.RoomUser.TradeId = 999;

        f.Trades.OfferItem(alice.Session, 100);
        f.Trades.OfferItems(alice.Session, new uint[] { 100 });
        f.Trades.RemoveItem(alice.Session, 100);

        Assert.Equal(new[] { ServerPacketHeader.TradingClosedComposer, ServerPacketHeader.TradingClosedComposer, ServerPacketHeader.TradingClosedComposer }, alice.Sent);
        Assert.Empty(bob.Sent);
    }

    [Fact]
    public void SingleOfferStopsAtTheLimitAndTheLtdBoundary()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Enumerable.Range(0, 10).Select(i => Floor((uint)(1000 + i), 11, ltd: true)).ToArray());

        for (uint id = 1000; id < 1010; id++) {
            f.Trades.OfferItem(alice.Session, id);
        }

        Assert.Equal(9, trade.Users[0].OfferedItems.Count); // the tenth LTD is refused
        Assert.Equal(10, alice.Sent.Count(header => header == ServerPacketHeader.TradingUpdateComposer)); // but still answered

        var plain = f.Join(3, 9);
        var second = f.Join(4, 10);
        var secondTrade = f.Start(plain, second);
        Stock(plain, Enumerable.Range(0, 501).Select(i => Floor((uint)(5000 + i), 12)).ToArray());

        for (uint id = 5000; id < 5501; id++) {
            f.Trades.OfferItem(plain.Session, id);
        }

        Assert.Equal(500, secondTrade.Users[0].OfferedItems.Count);
    }

    [Fact]
    public void ExplicitOfferSelectsThoseIdsAcrossTheSameDefinitionAndLtd()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        var firstRegular = Floor(200, 77);
        var ltd = Floor(201, 77, ltd: true);
        ltd.UniqueNumber = 4;
        ltd.UniqueSeries = 50;
        var secondRegular = Floor(202, 77);
        var otherLtd = Floor(203, 77, ltd: true);
        otherLtd.UniqueNumber = 9;
        Stock(alice, firstRegular, ltd, secondRegular, otherLtd, Floor(300, 78));
        f.Trades.Accept(alice.Session);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.OfferItems(alice.Session, new uint[] { 202, 201 });

        Assert.Equal(new uint[] { 202, 201 }, trade.Users[0].OfferedItems.Keys);
        Assert.Equal(0u, trade.Users[0].OfferedItems[202].UniqueNumber);
        Assert.Equal(4u, trade.Users[0].OfferedItems[201].UniqueNumber);
        Assert.Equal(50u, trade.Users[0].OfferedItems[201].UniqueSeries);
        Assert.DoesNotContain(200u, trade.Users[0].OfferedItems.Keys);
        Assert.DoesNotContain(203u, trade.Users[0].OfferedItems.Keys);
        Assert.DoesNotContain(300u, trade.Users[0].OfferedItems.Keys);
        Assert.False(trade.Users[0].HasAccepted);
        Assert.False(trade.Users[1].HasAccepted);
        Assert.True(trade.CanChange);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, alice.Sent);
        Assert.Equal(new[] { ServerPacketHeader.TradingUpdateComposer }, bob.Sent);
    }

    [Fact]
    public void ExplicitLtdIdsPastTheSingleOfferCapStayTheRequestedItems()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Enumerable.Range(0, 11).Select(i => Floor((uint)(600 + i), 90, ltd: true)).ToArray());
        var requested = new uint[] { 601, 603, 605, 607, 609, 600, 602, 604, 606, 608 };

        f.Trades.OfferItems(alice.Session, requested);

        Assert.Equal(requested, trade.Users[0].OfferedItems.Keys);
        Assert.All(trade.Users[0].OfferedItems.Values, item => Assert.True(item.UniqueNumber > 0));
        Assert.DoesNotContain(610u, trade.Users[0].OfferedItems.Keys);
    }

    [Fact]
    public void InvalidExplicitListsDoNotChangeTheOffer()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var trade = f.Start(alice, bob);
        Stock(alice, Floor(200, 77), Floor(201, 77), Floor(202, 77, ltd: true));
        f.Trades.OfferItem(alice.Session, 201);
        f.Trades.Accept(alice.Session);
        Assert.True(trade.Users[0].HasAccepted);
        alice.Packets.Clear();
        bob.Packets.Clear();

        f.Trades.OfferItems(alice.Session, new uint[] { 200, 200 });
        f.Trades.OfferItems(alice.Session, new uint[] { 200, 201 });
        f.Trades.OfferItems(alice.Session, new uint[] { 200, 999 });
        f.Trades.OfferItems(alice.Session, new uint[] { 999 });
        f.Trades.OfferItems(alice.Session, Array.Empty<uint>());

        Assert.Equal(new uint[] { 201 }, trade.Users[0].OfferedItems.Keys);
        Assert.Empty(trade.Users[1].OfferedItems);
        Assert.True(trade.Users[0].HasAccepted);
        Assert.False(trade.Users[1].HasAccepted);
        Assert.True(trade.CanChange);
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
    }

    [Fact]
    public void StaleNonParticipantNeverOperatesOnAnotherTradersSlot()
    {
        using var f = new TradeConfirmationServiceTests.TradeFixture();
        var alice = f.Join(1, 7);
        var bob = f.Join(2, 8);
        var stranger = f.Join(3, 9);
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
        alice.Packets.Clear();
        bob.Packets.Clear();
        stranger.Packets.Clear();

        f.Trades.OfferItem(stranger.Session, 400);
        f.Trades.OfferItems(stranger.Session, new uint[] { 401 });
        f.Trades.RemoveItem(stranger.Session, 100);
        f.Trades.RemoveItem(stranger.Session, 150);

        Assert.Equal(new uint[] { 100 }, trade.Users[0].OfferedItems.Keys);
        Assert.Equal(new uint[] { 150 }, trade.Users[1].OfferedItems.Keys);
        Assert.True(trade.Users[0].OfferedItems.ContainsKey(100));
        Assert.True(trade.Users[1].OfferedItems.ContainsKey(150));
        Assert.True(alice.RoomUser.HasStatus("trd"));
        Assert.True(bob.RoomUser.HasStatus("trd"));
        Assert.True(f.Trading.TryGetTrade(trade.Id, out _));
        Assert.True(trade.Users[0].HasAccepted); // a stale actor cannot reset alice's acceptance
        Assert.False(trade.Users[1].HasAccepted);
        Assert.True(trade.CanChange);
        Assert.Empty(alice.Sent);
        Assert.Empty(bob.Sent);
        Assert.Empty(stranger.Sent);
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

        foreach (var value in values) {
            var bytes = new byte[4];

            switch (value) {
                case int number:
                    BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                    stream.Write(bytes);
                    break;
                case uint number:
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, number);
                    stream.Write(bytes);
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private static byte[] OfferFrame(ushort header, int count, params uint[] itemIds)
    {
        var packet = new byte[10 + itemIds.Length * 4];
        BinaryPrimitives.WriteInt32BigEndian(packet, packet.Length - 4);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), header);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(6), count);

        for (var index = 0; index < itemIds.Length; index++) {
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(10 + index * 4), itemIds[index]);
        }

        return packet;
    }

    private static DispatchResult Dispatch(byte[] frame, Revision revision)
    {
        var calls = new List<string>();
        using var manager = new PacketManager([new TradingOfferItemsEvent(new RecordingOffers(calls))], NullLogger<PacketManager>.Instance);
        var disconnected = 0;
        var session = new FlashGameClient(new OfferDispatchServer(manager), new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = revision,
            DisconnectRequested = () => disconnected++
        };
        session.SetHabbo(new Habbo { Id = 1, Username = "Alice" });
        session.OnReceived(frame, 0, frame.Length);

        return new DispatchResult(calls, disconnected);
    }

    private sealed record DispatchResult(List<string> Calls, int Disconnects);

    private sealed class OfferDispatchServer(PacketManager manager) : IGameServer
    {
        public bool Start() => true;
        public bool Stop() => true;
        public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet) => manager.TryExecutePacket(client, messageId, packet);
        public bool ModifyOutgoingPacket(GameClient client, IOutgoingPacket packet) => true;
        public bool HasOutgoingPacketInjectors(uint messageId) => false;
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
        public void OfferItems(GameClient session, IReadOnlyList<uint> itemIds) => calls.Add($"OfferItems {string.Join(',', itemIds)}");
        public void RemoveItem(GameClient session, uint itemId) => calls.Add($"RemoveItem {itemId}");
    }
}
