using System.Collections.Immutable;
using System.Data.Common;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Crafting;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void CraftingPublishesOnlyAfterCommitAndReleasesExactlyItsInventoryReservations()
    {
        var (service, store, ingredients) = CraftingFixture();
        store.BeforeCommit = () =>
        {
            Assert.All(ingredients, item =>
            {
                Assert.Same(item, _client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
                Assert.False(item.TryReserve());
            });
            Assert.Empty(_client.Packets);
        };
        WithUnavailableItemGlobals(() => service.CraftSecret(_client, 10, [20, 21]));

        Assert.Equal(new uint[] { 20, 21 }, store.Consumed);
        Assert.All(ingredients, item => Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(item.Id)));
        Assert.Equal(102u, Assert.Single(_client.GetHabbo().Inventory.Furniture.GetItems).Definition.Id);
        Assert.Equal(new[] { ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.FurniListRemoveComposer,
            ServerPacketHeader.FurniListAddComposer, ServerPacketHeader.FurniListNotificationComposer,
            ServerPacketHeader.CraftingResultComposer, ServerPacketHeader.FurniListUpdateComposer }, _client.Sent);
        var result = new FlashIncomingPacket { Buffer = _client.Packets.Single(packet => packet.Header == ServerPacketHeader.CraftingResultComposer).Body };
        Assert.True(result.ReadBool());
        Assert.Equal("recipe", result.ReadString());
        Assert.Equal("product", result.ReadString());
        Assert.Equal("output", result.ReadString());
        Assert.False(result.HasDataRemaining());

        foreach (var item in ingredients) {
            Assert.True(item.TryReserve());
            item.ReleaseReservation();
        }
    }

    [Fact]
    public void CraftingCannotConsumeBusyDuplicateForeignOrTradedIngredients()
    {
        var (service, store, ingredients) = CraftingFixture();
        Assert.True(ingredients[1].TryReserve());
        service.CraftSecret(_client, 10, [20, 21]);
        Assert.Empty(store.Consumed);
        Assert.True(ingredients[0].TryReserve()); // Earlier acquisition was released.
        ingredients[0].ReleaseReservation();
        Assert.False(ingredients[1].TryReserve()); // It must not release someone else's reservation.
        ingredients[1].ReleaseReservation();
        service.CraftSecret(_client, 10, [20, 20]);
        ingredients[1].OwnerId = 8;
        service.CraftSecret(_client, 10, [20, 21]);
        ingredients[1].OwnerId = 7;
        _room.GetRoomUserManager().GetRoomUserByHabbo(7)!.IsTrading = true;
        service.CraftSecret(_client, 10, [20, 21]);
        Assert.Empty(store.Consumed);
        Assert.Equal(2, _client.GetHabbo().Inventory.Furniture.GetItems.Count());
        Assert.All(_client.Packets, packet =>
        {
            Assert.Equal(ServerPacketHeader.CraftingResultComposer, packet.Header);
            Assert.False(new FlashIncomingPacket { Buffer = packet.Body }.ReadBool());
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CraftingPreservesLimitedIngredientsForNormalAndSecretRecipes(bool secret, bool seriesOnly)
    {
        var (service, store, ingredients) = CraftingFixture(secret);
        ingredients[0].UniqueNumber = seriesOnly ? 0u : 1u;
        ingredients[0].UniqueSeries = seriesOnly ? 100u : 0u;

        if (secret) {
            service.CraftSecret(_client, 10, [20, 21]);
        }
        else {
            service.Craft(_client, 10, "recipe");
        }

        Assert.Empty(store.Consumed);
        Assert.All(ingredients, item => Assert.Same(item, _client.GetHabbo().Inventory.Furniture.GetItem(item.Id)));
        var result = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
        Assert.False(result.ReadBool());
        Assert.False(result.HasDataRemaining());
        Assert.True(ingredients[0].TryReserve());
        ingredients[0].ReleaseReservation();
    }

    [Fact]
    public void FailedCraftingWriteKeepsLiveInventoryAndReleasesReservations()
    {
        var (service, store, ingredients) = CraftingFixture();
        store.BeforeCommit = () => throw new CraftingFailure();
        service.CraftSecret(_client, 10, [20, 21]);
        Assert.Equal(2, _client.GetHabbo().Inventory.Furniture.GetItems.Count());
        Assert.Equal(ServerPacketHeader.CraftingResultComposer, Assert.Single(_client.Packets).Header);

        foreach (var item in ingredients) {
            Assert.True(item.TryReserve());
            item.ReleaseReservation();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void PublicAndDiscoveredRecipesCraftNormallyFromTheOwnedAltar(bool secret, bool discovered)
    {
        var (service, store, _) = CraftingFixture(secret, discovered);
        service.Craft(_client, 10, "recipe");
        Assert.Equal(new uint[] { 20, 21 }, store.Consumed);
        Assert.Equal(102u, Assert.Single(_client.GetHabbo().Inventory.Furniture.GetItems).Definition.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalCraftingRefusesForeignAltarsAndPreservesIngredientsOnFailure(bool failedWrite)
    {
        var (service, store, _) = CraftingFixture(false);

        if (failedWrite) {
            store.BeforeCommit = () => throw new CraftingFailure();
        }
        else {
            _room.GetRoomItemHandler().GetItem(10)!.OwnerId = 8;
        }

        service.Craft(_client, 10, "recipe");
        Assert.Empty(store.Consumed);
        Assert.Equal(2, _client.GetHabbo().Inventory.Furniture.GetItems.Count());
        Assert.Equal(ServerPacketHeader.CraftingResultComposer, Assert.Single(_client.Packets).Header);
    }

    [Fact]
    public void ACompletedDiscoveredRecipeRemainsCraftableInTheAvailabilityResponse()
    {
        var (service, _, _) = CraftingFixture(true, true);
        service.GetAvailable(_client, 10, [20, 21]);
        var packet = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
        Assert.Equal(0, packet.ReadInt());
        Assert.True(packet.ReadBool());
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public void CraftingFinishesItsInventoryMutationBeforeASendDisconnectsAndSkipsDetachedAchievements()
    {
        var (service, _, _) = CraftingFixture(award: () => throw new InvalidOperationException("Detached achievements must not run."));
        var habbo = _client.GetHabbo();
        _client.BeforeCapture = header =>
        {
            if (header == ServerPacketHeader.FurniListRemoveComposer) {
                Assert.Equal(102u, Assert.Single(habbo.Inventory.Furniture.GetItems).Definition.Id);
                typeof(GameClient).GetField("_habbo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(_client, null);
            }
        };
        service.CraftSecret(_client, 10, [20, 21]);
        Assert.Null(_client.GetHabbo());
        Assert.Equal(102u, Assert.Single(habbo.Inventory.Furniture.GetItems).Definition.Id);
    }

    [Fact]
    public void WallRewardsPublishTheWallInventoryNotificationType()
    {
        var (service, _, _) = CraftingFixture(wallReward: true);
        service.CraftSecret(_client, 10, [20, 21]);
        var packet = new FlashIncomingPacket
        {
            Buffer = _client.Packets.Single(packet => packet.Header == ServerPacketHeader.FurniListNotificationComposer).Body
        };
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(2, packet.ReadInt());
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(99u, packet.ReadUInt());
        Assert.False(packet.HasDataRemaining());
    }

    private (CraftingService Service, CraftingRecordingStore Store, InventoryItem[] Ingredients) CraftingFixture(bool secret = true, bool discovered = false, bool wallReward = false, Action? award = null)
    {
        var altar = Furni(10, InteractionType.None, WiredBoxType.None);
        altar.Definition.Id = 100;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, altar, 1, 1, 0, true, false, false));
        var habbo = _client.GetHabbo();
        habbo.Client = _client;
        habbo.Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) };
        Assert.True(_room.GetRoomUserManager().AddAvatarToRoom(_client));
        var input = new ItemDefinition { Id = 101, ItemName = "input", Type = ItemType.Floor, ProductType = "s" };
        var output = new ItemDefinition { Id = 102, ItemName = "output", Type = wallReward ? ItemType.Wall : ItemType.Floor, ProductType = "s" };
        var ingredients = new[] { new InventoryItem { Id = 20, OwnerId = 7, Definition = input }, new InventoryItem { Id = 21, OwnerId = 7, Definition = input } };

        foreach (var item in ingredients) {
            Assert.True(habbo.Inventory.Furniture.AddItem(item));
        }

        var store = new CraftingRecordingStore(secret, discovered);
        var service = new CraftingService(store, new CraftingDefinitions([input, output]),
            Proxy<IAchievementManager>((method, _) =>
            {
                if (method != "ProgressAchievement") {
                    throw new InvalidOperationException(method);
                }

                award?.Invoke();

                return false;
            }),
            TestLogging.For<CraftingService>());
        _client.Sent.Clear();
        _client.Packets.Clear();

        return (service, store, ingredients);
    }

    private sealed class CraftingRecordingStore(bool secret, bool discovered) : ICraftingStore
    {
        private readonly CraftingRecipe _recipe = new(1, "recipe", "product", 102, secret, discovered, null, "", [new(101, 2)]);
        public Action? BeforeCommit { get; set; }
        public uint[] Consumed { get; private set; } = [];
        public ImmutableArray<CraftingRecipe> Load(uint altarDefinitionId, int userId)
        {
            Assert.Equal((100u, 7), (altarDefinitionId, userId));

            return [_recipe];
        }
        public CraftingRecipe? Find(string code, int userId) => throw new NotSupportedException();
        public CraftedItem? Craft(int userId, uint roomId, uint altarId, CraftingRecipe recipe, IReadOnlyList<uint> ids, bool secretCraft)
        {
            Assert.Equal((7, 42u, 10u), (userId, roomId, altarId));
            Assert.Same(_recipe, recipe);
            BeforeCommit?.Invoke();
            Consumed = ids.ToArray();

            return new(99, recipe, recipe.Secret && !recipe.Discovered);
        }
    }

    private sealed class CraftingFailure() : DbException("forced crafting failure");

    private sealed class CraftingDefinitions(IEnumerable<ItemDefinition> definitions) : IItemDataManager
    {
        public Dictionary<uint, ItemDefinition> Items { get; } = definitions.ToDictionary(item => item.Id);
        public Dictionary<int, uint> Gifts => throw new NotSupportedException();
        public void Init() => throw new NotSupportedException();
        public ItemDefinition? GetItemByName(string name) => Items.Values.SingleOrDefault(item => item.ItemName == name);
    }
}
