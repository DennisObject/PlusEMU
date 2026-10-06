using System.Data;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Bots;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Users.Inventory.Pets;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogPurchaseServiceTests
{
    [Fact]
    public async Task HandlerDecodesAllFieldsBeforeDelegating()
    {
        var purchases = new RecordingPurchaseService();
        var habbo = Habbo();
        var (client, _) = HabbiconTestSupport.Client(habbo);

        await new PurchaseFromCatalogEvent(purchases).Parse(
            client, HabbiconTestSupport.Incoming(12, 34, "extra", 7));

        Assert.Equal(new CatalogPurchaseRequest(12, 34, "extra", 7), purchases.Request);
    }

    [Fact]
    public async Task DisabledCatalogDoesNotChargeOrCreateFurniture()
    {
        var context = Context(enabled: false);

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 1));

        Assert.Equal(0, context.Rewards.Charges);
        Assert.Equal(0, context.Factory.Creates);
        Assert.Empty(context.Habbo.Inventory.Furniture.AllItems);
        Assert.Empty(context.Tracks.Calls);
    }

    [Fact]
    public async Task FailedWalletChargeDoesNotCreateOrPublishFurniture()
    {
        var context = Context(chargeSucceeds: false);

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 1));

        Assert.Equal(1, context.Rewards.Charges);
        Assert.Equal(0, context.Factory.Creates);
        Assert.Empty(context.Habbo.Inventory.Furniture.AllItems);
        Assert.Empty(context.Tracks.Calls);
        Assert.DoesNotContain(context.Sent, packet => packet.Header == ServerPacketHeader.PurchaseOKComposer);
    }

    [Fact]
    public async Task OrdinaryPurchaseChargesCreatesAndPublishesInventoryItem()
    {
        var context = Context();

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 1));

        Assert.Equal(90, context.Habbo.Credits);
        Assert.Equal(1, context.Factory.Creates);
        Assert.NotNull(context.Habbo.Inventory.Furniture.GetItem(700));
        Assert.Equal((context.Client, Plus.HabboHotel.Quests.RewardTrackActions.BuyFromCatalogue, 1),
            Assert.Single(context.Tracks.Calls));
        Assert.Contains(context.Sent, packet => packet.Header == ServerPacketHeader.PurchaseOKComposer);
        Assert.Contains(context.Sent, packet => packet.Header == ServerPacketHeader.FurniListUpdateComposer);
    }

    [Fact]
    public async Task FurnitureBatchRewardsOnceAfterChargeAndInventoryPublication()
    {
        var context = Context();

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 3));

        Assert.Equal(70, context.Habbo.Credits);
        Assert.Equal(3, context.Factory.Creates);
        Assert.Equal(3, context.Habbo.Inventory.Furniture.AllItems.Count());
        Assert.Equal((context.Client, Plus.HabboHotel.Quests.RewardTrackActions.BuyFromCatalogue, 1),
            Assert.Single(context.Tracks.Calls));
    }

    [Fact]
    public async Task FactoryFailureDoesNotPublishFurniture()
    {
        var context = Context(factorySucceeds: false);

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 1));

        Assert.Equal(1, context.Factory.Creates);
        Assert.Empty(context.Habbo.Inventory.Furniture.AllItems);
        Assert.DoesNotContain(context.Sent, packet => packet.Header == ServerPacketHeader.FurniListNotificationComposer);
        Assert.Empty(context.Tracks.Calls);
    }

    [Fact]
    public async Task BotPurchaseCreatesInsideChargeBeforePublishingInOrder()
    {
        var context = Context(bot: true);
        context.BotStore.BeforeCreate = () =>
        {
            Assert.Equal(100, context.Habbo.Credits);
            Assert.Empty(context.Habbo.Inventory.Bots.Bots);
            Assert.Empty(context.Sent);
        };

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 1));

        Assert.Equal(1, context.Rewards.Charges);
        Assert.Same(context.Rewards.Connection, context.BotStore.Connection);
        Assert.Empty(context.Tracks.Calls);
        Assert.Same(context.Rewards.Transaction, context.BotStore.Transaction);
        Assert.Equal((50u, 42), (context.BotStore.Preset!.Id, context.BotStore.OwnerId));
        Assert.Equal(90, context.Habbo.Credits);
        Assert.Single(context.Habbo.Inventory.Bots.Bots);
        Assert.Equal(new[]
        {
            ServerPacketHeader.CreditBalanceComposer,
            ServerPacketHeader.BotInventoryComposer,
            ServerPacketHeader.FurniListNotificationComposer,
            ServerPacketHeader.PurchaseOKComposer,
            ServerPacketHeader.FurniListUpdateComposer
        }, context.Sent.Select(packet => packet.Header));
        var notification = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = context.Sent[2].Payload };
        Assert.Equal(1, notification.ReadInt());
        Assert.Equal(5, notification.ReadInt());
        Assert.Equal(1, notification.ReadInt());
        Assert.Equal(701u, notification.ReadUInt());
    }

    [Fact]
    public async Task BotAmountPreservesDiscountedChargeButCreatesExactlyOne()
    {
        var context = Context(bot: true);

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 7));

        Assert.Equal(40, context.Habbo.Credits);
        Assert.Equal(1, context.BotStore.Creates);
        Assert.Single(context.Habbo.Inventory.Bots.Bots);
    }

    [Fact]
    public async Task MissingBotPresetDoesNotChargeOrPublishSuccess()
    {
        var context = Context(bot: true, botPreset: false);

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 1));

        Assert.Equal(0, context.Rewards.Charges);
        Assert.Equal(0, context.BotStore.Creates);
        Assert.Equal(100, context.Habbo.Credits);
        Assert.Empty(context.Habbo.Inventory.Bots.Bots);
        var error = Assert.Single(context.Sent);
        Assert.Equal(ServerPacketHeader.BroadcastMessageAlertComposer, error.Header);
        var payload = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = error.Payload };
        Assert.Equal("Oops! There was an error whilst purchasing this bot. It seems that there is no bot data for the bot!", payload.ReadString());
        Assert.Equal("", payload.ReadString());
        Assert.DoesNotContain(context.Sent, packet => packet.Header == ServerPacketHeader.PurchaseOKComposer);
        Assert.DoesNotContain(context.Sent, packet => packet.Header == ServerPacketHeader.FurniListUpdateComposer);
    }

    [Fact]
    public async Task RefusedBotChargeNeverCreatesOrPublishes()
    {
        var context = Context(bot: true, chargeSucceeds: false);

        await context.Service.Purchase(context.Client, new(1, 2, "ignored", 1));

        Assert.Equal(1, context.Rewards.Charges);
        Assert.Equal(0, context.BotStore.Creates);
        Assert.Empty(context.Habbo.Inventory.Bots.Bots);
        Assert.Empty(context.Sent);
    }

    [Fact]
    public async Task BotStoreFailurePropagatesBeforeWalletOrPublication()
    {
        var context = Context(bot: true, botStoreFails: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Service.Purchase(context.Client, new(1, 2, "ignored", 1)));

        Assert.Equal(1, context.BotStore.Creates);
        Assert.Equal(100, context.Habbo.Credits);
        Assert.Empty(context.Habbo.Inventory.Bots.Bots);
        Assert.Empty(context.Sent);
    }

    [Fact]
    public async Task ClubPurchasePublishesPreparedOfferAndMembershipResponses()
    {
        var context = Context(club: true);

        await context.Service.Purchase(context.Client, new(1, 9, "", 1));

        Assert.Contains(context.Sent, packet => packet.Header == ServerPacketHeader.PurchaseOKComposer);
        Assert.Contains(context.Sent, packet => packet.Header == ServerPacketHeader.HabboClubOffersComposer);
        Assert.Contains(context.Sent, packet => packet.Header == ServerPacketHeader.ScrSendUserInfoComposer);
    }

    private static TestContext Context(bool enabled = true, bool chargeSucceeds = true,
        bool factorySucceeds = true, bool club = false, bool bot = false, bool botPreset = true,
        bool botStoreFails = false)
    {
        var definition = new ItemDefinition
        {
            Id = 50,
            ItemName = "chair",
            ProductType = bot ? "r" : "s",
            Type = ItemType.Floor,
            InteractionType = InteractionType.None
        };
        var offer = new CatalogItem
        {
            Id = 2,
            OfferId = 2,
            Definition = definition,
            Amount = 1,
            CostCredits = 10,
            HaveOffer = true,
            CatalogName = "chair"
        };
        var page = new CatalogPage { Id = 1, Enabled = true, Layout = club ? "club_buy" : "default_3x3" };
        page.Offers.Add(2, offer);
        var catalog = Proxy<ICatalogManager, CatalogProxy>();
        var catalogProxy = (CatalogProxy)(object)catalog;
        catalogProxy.Page = page;
        catalogProxy.ClubOffer = new ClubOffer { Id = 9, Name = "HC_31", Days = 31 };
        catalogProxy.Bot = botPreset ? new CatalogBot
        {
            Id = definition.Id,
            Name = "Catalog Bot",
            Motto = "A bot motto",
            Figure = "hd-180-1.ch-210-66",
            Gender = "M",
            AiType = "generic"
        } : null;
        var rewards = new RecordingRewards(chargeSucceeds);
        var factory = new RecordingFactory(factorySucceeds);
        var botStore = new RecordingBotStore(botStoreFails);
        var habbo = Habbo();
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var tracks = new TestRewardProgress((target, action, amount) =>
        {
            Assert.Same(client, target);
            Assert.Equal(Plus.HabboHotel.Quests.RewardTrackActions.BuyFromCatalogue, action);
            Assert.Equal(1, amount);
            Assert.Equal(1, rewards.Charges);
            Assert.True(habbo.Credits < 100);
            Assert.NotEmpty(habbo.Inventory.Furniture.AllItems);
            Assert.Contains(sent, packet => packet.Header == ServerPacketHeader.FurniListNotificationComposer);
            Assert.DoesNotContain(sent, packet => packet.Header == ServerPacketHeader.PurchaseOKComposer);
        });
        var service = new CatalogPurchaseService(
            catalog,
            new Settings(enabled),
            Proxy<IAchievementManager, EmptyProxy>(),
            Proxy<IItemDataManager, EmptyProxy>(),
            Proxy<IBadgeManager, EmptyProxy>(),
            factory,
            botStore,
            Proxy<IHabbiconService, EmptyProxy>(),
            new RecordingMembership(),
            rewards,
            tracks,
            Proxy<IAvatarEffectStore, EmptyProxy>(),
            new FixedClock(new DateTimeOffset(2040, 2, 3, 4, 5, 6, TimeSpan.Zero)),
            NullLogger<CatalogPurchaseService>.Instance);
        return new(service, client, habbo, sent, rewards, factory, botStore, tracks);
    }

    private static Habbo Habbo() => new()
    {
        Id = 42,
        Username = "Dennis",
        Credits = 100,
        Inventory = new InventoryComponent
        {
            Badges = new BadgesInventoryComponent(new()),
            Furniture = new FurnitureInventoryComponent(Array.Empty<InventoryItem>(), Array.Empty<InventoryItem>()),
            Pets = new PetsInventoryComponent(new()),
            Bots = new BotInventoryComponent(new())
        }
    };

    private static TInterface Proxy<TInterface, TProxy>()
        where TInterface : class
        where TProxy : DispatchProxy => DispatchProxy.Create<TInterface, TProxy>();

    private sealed record TestContext(
        CatalogPurchaseService Service,
        GameClient Client,
        Habbo Habbo,
        List<(uint Header, byte[] Payload)> Sent,
        RecordingRewards Rewards,
        RecordingFactory Factory,
        RecordingBotStore BotStore,
        TestRewardProgress Tracks);

    private sealed class RecordingPurchaseService : ICatalogPurchaseService
    {
        public CatalogPurchaseRequest? Request { get; private set; }
        public Task Purchase(GameClient session, CatalogPurchaseRequest request)
        {
            Request = request;
            return Task.CompletedTask;
        }
    }

    private sealed class Settings(bool enabled) : ISettingsManager
    {
        public string TryGetValue(string value) => enabled ? "1" : "0";
        public string? GetOptionalValue(string key) => TryGetValue(key);
        public Task Reload() => Task.CompletedTask;
    }

    private sealed class RecordingRewards(bool succeeds) : IClubRewards
    {
        public int Charges { get; private set; }
        public IDbConnection Connection { get; } = Proxy<IDbConnection, EmptyProxy>();
        public IDbTransaction Transaction { get; } = Proxy<IDbTransaction, EmptyProxy>();
        public bool Charge(Habbo habbo, int credits, int duckets = 0, int diamonds = 0,
            Func<IDbConnection, IDbTransaction, bool>? deliver = null, bool kickbackEligible = true)
        {
            Charges++;
            if (!succeeds || deliver?.Invoke(Connection, Transaction) == false) return false;
            habbo.Credits -= credits;
            habbo.Duckets -= duckets;
            habbo.Diamonds -= diamonds;
            return true;
        }
        public ClubGiftInfo Gifts(Habbo habbo) => throw new NotSupportedException();
        public ClubGiftClaim? Claim(Habbo habbo, string productCode) => throw new NotSupportedException();
        public ClubKickback Kickback(Habbo habbo) => throw new NotSupportedException();
        public void RunPaydays() => throw new NotSupportedException();
    }

    private sealed class RecordingBotStore(bool fail) : ICatalogBotPurchaseStore
    {
        public Action? BeforeCreate { get; set; }
        public int Creates { get; private set; }
        public IDbConnection? Connection { get; private set; }
        public IDbTransaction? Transaction { get; private set; }
        public CatalogBot? Preset { get; private set; }
        public int OwnerId { get; private set; }

        public Bot Create(IDbConnection connection, IDbTransaction transaction, CatalogBot preset, int ownerId)
        {
            BeforeCreate?.Invoke();
            Creates++;
            (Connection, Transaction, Preset, OwnerId) = (connection, transaction, preset, ownerId);
            if (fail) throw new InvalidOperationException("forced bot store failure");
            return new Bot(701, ownerId, preset.Name!, preset.Motto!, preset.Figure!, preset.Gender!);
        }
    }

    private sealed class RecordingFactory(bool succeeds) : IItemFactory
    {
        public int Creates { get; private set; }
        public Item CreateSingleItemNullable(ItemDefinition definition, Habbo habbo, string extraData,
            string displayFlags, int groupId = 0, uint limitedNumber = 0, uint limitedStack = 0)
        {
            Creates++;
            if (!succeeds) return null!;
            return new Item { Id = (uint)(699 + Creates), OwnerId = (uint)habbo.Id, Definition = definition };
        }
        public Item CreateSingleItem(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags,
            uint itemId, uint limitedNumber = 0, uint limitedStack = 0) => throw new NotSupportedException();
        public Item CreateGiftItem(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags,
            int itemId, uint limitedNumber = 0, uint limitedStack = 0) => throw new NotSupportedException();
        public List<Item> CreateMultipleItems(ItemDefinition definition, Habbo habbo, string extraData, int amount,
            int groupId = 0)
        {
            var items = new List<Item>();
            for (var i = 0; i < amount; i++)
            {
                var item = CreateSingleItemNullable(definition, habbo, extraData, extraData, groupId);
                if (item != null) items.Add(item);
            }
            return items;
        }
        public List<Item> CreateMultipleItems(ItemDefinition definition, int ownerId, string extraData, int amount,
            int groupId = 0) => throw new NotSupportedException();
        public List<Item> CreateTeleporterItems(ItemDefinition definition, Habbo habbo, int groupId = 0) =>
            throw new NotSupportedException();
        public void CreateMoodlightData(Item item) => throw new NotSupportedException();
        public void CreateTonerData(Item item) => throw new NotSupportedException();
    }

    public class CatalogProxy : DispatchProxy
    {
        public CatalogPage Page { get; set; } = null!;
        public ClubOffer ClubOffer { get; set; } = null!;
        public CatalogBot? Bot { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICatalogManager.TryGetPage))
            {
                args![1] = Page;
                return (int)args[0]! == Page.Id;
            }
            if (targetMethod?.Name == nameof(ICatalogManager.TryGetClubOffer))
            {
                args![1] = ClubOffer;
                return (int)args[0]! == ClubOffer.Id;
            }
            if (targetMethod?.Name == nameof(ICatalogManager.TryGetBot))
            {
                args![1] = Bot;
                return Bot != null && (uint)args[0]! == Bot.Id;
            }
            if (targetMethod?.Name == "get_ClubOffers") return new[] { ClubOffer };
            return Default(targetMethod?.ReturnType);
        }
    }

    private sealed class RecordingMembership : IClubMembershipService
    {
        public DateTimeOffset? GetExpiry(int userId) => null;
        public DateTimeOffset? Purchase(Habbo habbo, ClubOffer offer, int? recipientId = null) =>
            new DateTimeOffset(2040, 3, 5, 4, 5, 6, TimeSpan.Zero);
        public DateTimeOffset? Grant(Habbo actor, int userId, int days) => throw new NotSupportedException();
    }

    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Default(targetMethod?.ReturnType);
    }

    private static object? Default(Type? type)
    {
        if (type == null || type == typeof(void)) return null;
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GenericTypeArguments[0])
                .Invoke(null, new[] { type.GenericTypeArguments[0].IsValueType ? Activator.CreateInstance(type.GenericTypeArguments[0]) : null });
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
