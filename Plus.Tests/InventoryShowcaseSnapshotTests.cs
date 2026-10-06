using Plus.Communication.Packets.Incoming.Inventory.Badges;
using Plus.Communication.Packets.Incoming.Inventory.Bots;
using Plus.Communication.Packets.Incoming.Inventory.Pets;
using Plus.Communication.Packets.Outgoing.Inventory.Badges;
using Plus.Communication.Packets.Outgoing.Inventory.Bots;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Bots;
using Xunit;

namespace Plus.Tests;

public sealed class InventoryShowcaseSnapshotTests
{
    [Fact]
    public void BotWireFieldsRemainFrozenAfterSourceMutation()
    {
        var bot = new Bot(7, 42, "name", "motto", "figure", "m");
        var source = new List<Bot> { bot };
        var composer = new BotInventoryComposer(BotInventorySnapshot.Capture(source));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[] { 1, 7, "name", "motto", "m", "figure" }, before.Writes);
        bot.Id = 99;
        bot.Name = "changed";
        bot.Motto = "changed";
        bot.Gender = "f";
        bot.Figure = "changed";
        source.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public void BadgeInventoryKeepsInsertionOrderAndEquippedSlotOrderWithoutLateReads()
    {
        var badge = new Badge("second", 2);
        var source = new List<Badge> { badge, new("unworn", 0), new("first", 1) };
        var composer = new BadgesComposer(BadgeInventorySnapshot.Capture(source));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[] { 3, 1, "second", 1, "unworn", 1, "first", 2, 1, "first", 2, "second" }, before.Writes);
        badge.Code = "changed";
        badge.Slot = 0;
        source.Clear();
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public async Task InventoryHandlersDelegateWithoutReadingSessionOrPacket()
    {
        var inventory = new RecordingInventory();
        await new GetBotInventoryEvent(inventory).Parse(null!, null!);
        await new GetPetInventoryEvent(inventory).Parse(null!, null!);
        await new GetBadgesEvent(inventory).Parse(null!, null!);
        Assert.Equal(new[] { "bots", "pets", "badges" }, inventory.Calls);
    }

    private sealed class RecordingInventory : IInventoryShowcaseService
    {
        public List<string> Calls { get; } = [];
        public void ShowBots(GameClient session) => Calls.Add("bots");
        public void ShowPets(GameClient session) => Calls.Add("pets");
        public void ShowBadges(GameClient session) => Calls.Add("badges");
    }
}
