using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Trade game fixture")]
public sealed class TradeSettingsDependencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionUsesInjectedLiveRedemptionSettingWithGlobalUnavailable(bool redeem)
    {
        var settings = new TestRoomSettings(new() { ["trading.auto_exchange_redeemables"] = redeem ? "0" : "1" });
        using var fixture = new TradeConfirmationServiceTests.TradeFixture(settings);
        var alice = fixture.Join(1, 7);
        var bob = fixture.Join(2, 8);
        bob.Habbo.Credits = 7;
        var voucher = new InventoryItem
        {
            Id = 100,
            OwnerId = 1,
            Definition = new ItemDefinition
            {
                Type = ItemType.Floor,
                InteractionType = InteractionType.Exchange,
                SpriteId = 11,
                BehaviourData = 10
            }
        };
        Assert.True(alice.Habbo.Inventory.Furniture.AddItem(voucher));
        alice.Packets.Clear();
        bob.Packets.Clear();
        var trade = fixture.Start(alice, bob);
        trade.Users[0].OfferedItems.Add(voucher.Id, voucher);
        settings.Values["trading.auto_exchange_redeemables"] = redeem ? "1" : "0";
        var global = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = global.GetValue(null);

        try {
            global.SetValue(null, null);
            trade.Finish();
        }
        finally {
            global.SetValue(null, previous);
        }

        Assert.Null(alice.Habbo.Inventory.Furniture.GetItem(voucher.Id));
        Assert.Equal(redeem ? 17 : 7, bob.Habbo.Credits);

        if (redeem) {
            Assert.Null(bob.Habbo.Inventory.Furniture.GetItem(voucher.Id));
            Assert.Equal(new uint[] { ServerPacketHeader.CreditBalanceComposer, ServerPacketHeader.TradingFinishComposer }, bob.Sent);
            var packet = new FlashIncomingPacket { Buffer = bob.Packets[0].Payload };
            Assert.Equal("17.0", packet.ReadString());
            Assert.False(packet.HasDataRemaining());
        }
        else {
            Assert.Same(voucher, bob.Habbo.Inventory.Furniture.GetItem(voucher.Id));
            Assert.Equal(new uint[] { ServerPacketHeader.FurniListAddComposer, ServerPacketHeader.FurniListNotificationComposer,
                ServerPacketHeader.TradingFinishComposer }, bob.Sent);
        }

        Assert.Equal(new uint[] { ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.TradingFinishComposer }, alice.Sent);
        Assert.Equal(new[] { (1, 2, "100;", "") }, fixture.Store.Logged);
        Assert.False(fixture.Trading.TryGetTrade(trade.Id, out _));
    }
}
