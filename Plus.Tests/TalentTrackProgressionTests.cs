using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Talents;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class TalentTrackProgressionTests
{
    [Fact]
    public void CompletedLevelsPublishTheirCommittedInventoryAndNotifyOnlyOnce()
    {
        var habbo = User();
        habbo.Achievements.TryAdd("ACH_A", new("ACH_A", 1, 0));
        var (client, packets) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        var calls = 0;
        var gift = new ItemDefinition { Id = 50, ItemName = "gift", Type = ItemType.Floor };
        var store = CatalogSnapshotTestSupport.Proxy<ITalentTrackRewardStore>((method, args) => {
            Assert.Equal("Claim", method);
            Assert.Equal((7, "citizenship", 0), ((int)args[0]!, (string)args[1]!, (int)args[2]!));
            Assert.Same(gift, Assert.Single((IReadOnlyCollection<ItemDefinition>)args[3]!));
            calls++;
            return new[] { new InventoryItem { Id = 77, OwnerId = 7, Definition = gift } };
        });
        var service = Service(store, gift);
        var capture = client.SendCallback;
        client.SendCallback = args => {
            Assert.True(habbo.Inventory.Furniture.HasItem(77));
            return capture!(args);
        };
        service.Progress(habbo, TalentTrackPresentationTests.Achievements());
        service.Progress(habbo, TalentTrackPresentationTests.Achievements());
        Assert.Equal(1, calls);
        Assert.Single(habbo.Inventory.Furniture.AllItems);
        Assert.Equal(new[] { ServerPacketHeader.FurniListNotificationComposer, ServerPacketHeader.FurniListUpdateComposer, ServerPacketHeader.TalentLevelUpComposer }, packets.Select(packet => packet.Header));
        packets.Clear();
        // A fresh service must honor existing durable claims without notifying again.
        var returning = Service(CatalogSnapshotTestSupport.Proxy<ITalentTrackRewardStore>((method, _) => {
            Assert.Equal("Claim", method);
            return null;
        }), gift);
        returning.Progress(habbo, TalentTrackPresentationTests.Achievements());
        Assert.Empty(packets);
    }

    [Fact]
    public void RewardFailureLeavesInventoryAndPacketStreamUnchangedAndIsRetryable()
    {
        var habbo = User();
        habbo.Achievements.TryAdd("ACH_A", new("ACH_A", 1, 0));
        var (client, packets) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        var calls = 0;
        var gift = new ItemDefinition { Id = 50, ItemName = "gift", Type = ItemType.Floor };
        var service = Service(CatalogSnapshotTestSupport.Proxy<ITalentTrackRewardStore>((method, _) => {
            Assert.Equal("Claim", method);
            calls++;
            throw new InvalidOperationException("write failed");
        }), gift);
        Assert.Throws<InvalidOperationException>(() => service.Progress(habbo, TalentTrackPresentationTests.Achievements()));
        Assert.Throws<InvalidOperationException>(() => service.Progress(habbo, TalentTrackPresentationTests.Achievements()));
        Assert.Equal(2, calls);
        Assert.Empty(habbo.Inventory.Furniture.AllItems);
        Assert.Empty(packets);
    }

    [Fact]
    public void UnresolvedConfiguredRewardCannotConsumeAClaim()
    {
        var habbo = User();
        habbo.Achievements.TryAdd("ACH_A", new("ACH_A", 1, 0));
        var service = Service(CatalogSnapshotTestSupport.Proxy<ITalentTrackRewardStore>((method, _) =>
            throw new InvalidOperationException("No store access expected")), null);
        service.Progress(habbo, TalentTrackPresentationTests.Achievements());
        Assert.Empty(habbo.Inventory.Furniture.AllItems);
    }

    [Fact]
    public void ClosedWalletRejectsRewardsBeforeReadingDefinitionsOrCallingPersistence()
    {
        var habbo = User();
        typeof(Habbo).GetField("_habboSaved", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(habbo, true);
        var service = new TalentTrackProgressionService(
            CatalogSnapshotTestSupport.Proxy<ITalentTrackManager>((_, _) => throw new InvalidOperationException("No definitions expected")),
            CatalogSnapshotTestSupport.Proxy<IItemDataManager>((_, _) => throw new InvalidOperationException("No gifts expected")),
            CatalogSnapshotTestSupport.Proxy<ITalentTrackRewardStore>((_, _) => throw new InvalidOperationException("No writes expected")),
            NullLogger<TalentTrackProgressionService>.Instance);
        service.Progress(habbo, TalentTrackPresentationTests.Achievements());
    }

    private static Habbo User() => new() { Id = 7, Inventory = new InventoryComponent { Furniture = new([], []) } };
    private static TalentTrackProgressionService Service(ITalentTrackRewardStore store, ItemDefinition? gift) => new(
        new TalentTrackPresentationTests.FixedTalents(TalentTrackPresentationTests.Levels()),
        CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) => method == "GetItemByName" ? gift : throw new InvalidOperationException(method)),
        store, NullLogger<TalentTrackProgressionService>.Instance);
}
