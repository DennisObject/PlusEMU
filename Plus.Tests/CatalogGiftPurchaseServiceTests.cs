using System.Data;
using System.Reflection;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Bots;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Users.Inventory.Pets;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogGiftPurchaseServiceTests
{
    [Fact]
    public async Task HandlerDecodesCompletePrimitiveRequest()
    {
        var service = new RecordingService();
        var (client, _) = HabbiconTestSupport.Client(Habbo(1, "sender"));

        await new PurchaseFromCatalogAsGiftEvent(service).Parse(client,
            HabbiconTestSupport.Incoming(1, 2, "data", "recipient", "message", 3, 4, 5, true));

        Assert.Equal(new CatalogGiftPurchaseRequest(1, 2, "data", "recipient", "message", 3, 4, 5, true), service.Request);
    }

    [Fact]
    public async Task DisabledCatalogDoesNotResolveRecipientOrCharge()
    {
        var context = CreateContext(enabled: false);

        await context.Service.Purchase(context.SenderClient, Request());

        Assert.Equal(0, context.Rewards.Charges);
        Assert.Equal(0, context.Clients.Lookups);
    }

    [Fact]
    public async Task SelfAndUnknownRecipientsAreDeniedWithoutCharge()
    {
        var self = CreateContext(recipientIsSender: true);
        await self.Service.Purchase(self.SenderClient, Request(recipient: "sender"));
        Assert.Equal(0, self.Rewards.Charges);

        var missing = CreateContext(missingRecipient: true);
        await missing.Service.Purchase(missing.SenderClient, Request());
        Assert.Equal(0, missing.Rewards.Charges);
        Assert.Contains(missing.SenderPackets, packet => packet.Header == ServerPacketHeader.GiftWrappingErrorComposer);

        var privateRecipient = CreateContext(recipientAllowsGifts: false);
        await privateRecipient.Service.Purchase(privateRecipient.SenderClient, Request());
        Assert.Equal(0, privateRecipient.Rewards.Charges);
        Assert.Empty(privateRecipient.Recipient.Inventory.Furniture.AllItems);
    }

    [Fact]
    public async Task InsufficientBalanceAndExactThrottleBoundaryDoNotPersist()
    {
        var poor = CreateContext();
        poor.Sender.Credits = 9;
        await poor.Service.Purchase(poor.SenderClient, Request());
        Assert.Equal(0, poor.Store.Creates);
        Assert.Contains(poor.SenderPackets, packet => packet.Header == ServerPacketHeader.PresentDeliverErrorComposer);

        var throttled = CreateContext();
        throttled.Sender.LastGiftPurchasedAt = throttled.Clock.Now.AddSeconds(-15);
        await throttled.Service.Purchase(throttled.SenderClient, Request());
        Assert.Equal(0, throttled.Store.Creates);
        Assert.Equal(1, throttled.Sender.GiftPurchasingWarnings);
    }

    [Fact]
    public async Task OrdinaryGiftPersistsBeforePublishingRecipientInventory()
    {
        var context = CreateContext();

        await context.Service.Purchase(context.SenderClient, Request());

        Assert.Equal(1, context.Store.Creates);
        Assert.Equal(90, context.Sender.Credits);
        Assert.Equal(context.Clock.Now, context.Sender.LastGiftPurchasedAt);
        Assert.NotNull(context.Recipient.Inventory.Furniture.GetItem(700));
        Assert.Contains(context.RecipientPackets, packet => packet.Header == ServerPacketHeader.FurniListAddComposer);
        Assert.Contains(context.SenderPackets, packet => packet.Header == ServerPacketHeader.PurchaseOKComposer);
    }

    [Fact]
    public async Task StoreFailureLeavesWalletInventoryThrottleAndPacketsUnchanged()
    {
        var context = CreateContext(storeFails: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.Purchase(context.SenderClient, Request()));

        Assert.Equal(100, context.Sender.Credits);
        Assert.Null(context.Sender.LastGiftPurchasedAt);
        Assert.Empty(context.Recipient.Inventory.Furniture.AllItems);
        Assert.Empty(context.RecipientPackets);
        Assert.Empty(context.SenderPackets);
    }

    private static CatalogGiftPurchaseRequest Request(string recipient = "recipient") =>
        new(1, 2, "", recipient, "hello", 3, 4, 5, true);

    private static Context CreateContext(bool enabled = true, bool missingRecipient = false,
        bool recipientIsSender = false, bool recipientAllowsGifts = true, bool storeFails = false)
    {
        var content = new ItemDefinition
        {
            Id = 50,
            ItemName = "chair",
            ProductType = "s",
            Type = ItemType.Floor,
            InteractionType = InteractionType.None,
            AllowGift = true
        };
        var present = new ItemDefinition
        {
            Id = 60,
            ItemName = "gift_box",
            ProductType = "s",
            Type = ItemType.Floor,
            InteractionType = InteractionType.Gift
        };
        var offer = new CatalogItem
        {
            Id = 2,
            OfferId = 2,
            Definition = content,
            Amount = 1,
            CostCredits = 10,
            HaveOffer = true,
            CatalogName = "chair"
        };
        var page = new CatalogPage { Id = 1, Enabled = true, Layout = "default_3x3" };
        page.Offers.Add(2, offer);
        var catalog = Proxy<ICatalogManager, CatalogProxy>();
        ((CatalogProxy)(object)catalog).Page = page;
        var items = Proxy<IItemDataManager, ItemDataProxy>();
        var itemProxy = (ItemDataProxy)(object)items;
        itemProxy.Gifts = new Dictionary<int, uint> { [3] = present.Id };
        itemProxy.Items = new Dictionary<uint, ItemDefinition> { [present.Id] = present };

        var sender = Habbo(1, "sender");
        var recipient = recipientIsSender ? sender : Habbo(2, "recipient");
        recipient.AllowGifts = recipientAllowsGifts;
        var (senderClient, senderPackets) = HabbiconTestSupport.Client(sender);
        var recipientClient = recipientIsSender ? senderClient : HabbiconTestSupport.Client(recipient).Client;
        var recipientPackets = recipientIsSender ? senderPackets : Capture(recipientClient);
        var clients = new ClientLookup(senderClient, recipientClient, missingRecipient);
        var rewards = new Rewards();
        var store = new GiftStore(storeFails, present);
        var clock = new FixedClock(new DateTimeOffset(2040, 2, 3, 4, 5, 6, TimeSpan.Zero));
        var service = new CatalogGiftPurchaseService(
            catalog,
            new Settings(enabled),
            items,
            Proxy<IAchievementManager, EmptyProxy>(),
            clients,
            Proxy<IQuestManager, EmptyProxy>(),
            Proxy<IClubMembershipService, EmptyProxy>(),
            rewards,
            store,
            clock);
        return new(service, senderClient, sender, recipient, senderPackets, recipientPackets, clients, rewards, store, clock);
    }

    private static List<(uint Header, byte[] Payload)> Capture(GameClient client)
    {
        var packets = new List<(uint, byte[])>();
        ((Plus.Communication.Flash.FlashGameClient)client).SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add((System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));
            return true;
        };
        return packets;
    }

    private static Habbo Habbo(int id, string name) => new()
    {
        Id = id,
        Username = name,
        Credits = 100,
        Duckets = 100,
        Diamonds = 100,
        AllowGifts = true,
        Inventory = new InventoryComponent
        {
            Badges = new BadgesInventoryComponent(new()),
            Furniture = new FurnitureInventoryComponent(Array.Empty<InventoryItem>(), Array.Empty<InventoryItem>()),
            Pets = new PetsInventoryComponent(new()),
            Bots = new BotInventoryComponent(new())
        }
    };

    private sealed record Context(
        CatalogGiftPurchaseService Service,
        GameClient SenderClient,
        Habbo Sender,
        Habbo Recipient,
        List<(uint Header, byte[] Payload)> SenderPackets,
        List<(uint Header, byte[] Payload)> RecipientPackets,
        ClientLookup Clients,
        Rewards Rewards,
        GiftStore Store,
        FixedClock Clock);

    private sealed class RecordingService : ICatalogGiftPurchaseService
    {
        public CatalogGiftPurchaseRequest? Request { get; private set; }
        public Task Purchase(GameClient session, CatalogGiftPurchaseRequest request)
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

    private sealed class ClientLookup(GameClient sender, GameClient recipient, bool missing) : IGameClientManager
    {
        public int Lookups { get; private set; }
        public GameClient? GetClientByUsername(string username)
        {
            Lookups++;
            if (missing) return null;
            return username.Equals(sender.GetHabbo().Username, StringComparison.OrdinalIgnoreCase) ? sender : recipient;
        }
        public GameClient? GetClientByUserId(int userId) => userId == recipient.GetHabbo().Id ? recipient : null;
        public int Count => 2;
        public ICollection<GameClient> GetClients => new[] { sender, recipient };
        public void OnCycle() { }
        public bool TryGetClient(Guid clientId, out GameClient? client) { client = null; return false; }
        public bool TryChangeClientUsername(GameClient client, string oldUsername, string newUsername, Func<bool> persist) => false;
        public Task<string> GetNameById(int id) => Task.FromResult(string.Empty);
        public IEnumerable<GameClient> GetClientsById(Dictionary<int, Plus.HabboHotel.Users.Messenger.MessengerBuddy>.KeyCollection users) => Array.Empty<GameClient>();
        public void StaffAlert(Plus.Communication.Packets.IServerPacket message, int exclude = 0) { }
        public void ModAlert(string message) { }
        public void DoAdvertisingReport(GameClient reporter, GameClient target) { }
        public void SendPacket(Plus.Communication.Packets.IServerPacket packet, Plus.HabboHotel.Permissions.PermissionDefinition? permission = null) { }
        public void LogClonesOut(int userId) { }
        public void RegisterClient(GameClient client, int userId, string username) { }
        public void UnregisterClient(GameClient client, int userId, string username) { }
        public void CloseAll() { }
    }

    private sealed class Rewards : IClubRewards
    {
        public int Charges { get; private set; }
        public bool Charge(Habbo habbo, int credits, int duckets = 0, int diamonds = 0,
            Func<IDbConnection, IDbTransaction, bool>? deliver = null, bool kickbackEligible = true)
        {
            Charges++;
            var oldCredits = habbo.Credits;
            try
            {
                if (deliver?.Invoke(null!, null!) == false) return false;
                habbo.Credits -= credits;
                habbo.Duckets -= duckets;
                habbo.Diamonds -= diamonds;
                return true;
            }
            catch
            {
                habbo.Credits = oldCredits;
                throw;
            }
        }
        public ClubGiftInfo Gifts(Habbo habbo) => throw new NotSupportedException();
        public ClubGiftClaim? Claim(Habbo habbo, string productCode) => throw new NotSupportedException();
        public ClubKickback Kickback(Habbo habbo) => throw new NotSupportedException();
        public void RunPaydays() => throw new NotSupportedException();
    }

    private sealed class GiftStore(bool fails, ItemDefinition present) : ICatalogGiftStore
    {
        public int Creates { get; private set; }
        public InventoryItem Create(IDbConnection connection, IDbTransaction transaction, int recipientId,
            ItemDefinition presentDefinition, ItemDefinition contentDefinition, string presentExtraData, string contentExtraData)
        {
            Creates++;
            if (fails) throw new InvalidOperationException("store failed");
            return new InventoryItem { Id = 700, OwnerId = (uint)recipientId, Definition = present };
        }
    }

    private static TInterface Proxy<TInterface, TProxy>() where TInterface : class where TProxy : DispatchProxy =>
        DispatchProxy.Create<TInterface, TProxy>();

    public class CatalogProxy : DispatchProxy
    {
        public CatalogPage Page { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICatalogManager.TryGetPage))
            {
                args![1] = Page;
                return (int)args[0]! == Page.Id;
            }
            return Default(targetMethod?.ReturnType);
        }
    }

    public class ItemDataProxy : DispatchProxy
    {
        public Dictionary<int, uint> Gifts { get; set; } = new();
        public Dictionary<uint, ItemDefinition> Items { get; set; } = new();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Gifts" => Gifts,
            "get_Items" => Items,
            _ => Default(targetMethod?.ReturnType)
        };
    }

    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Default(targetMethod?.ReturnType);
    }

    private static object? Default(Type? type)
    {
        if (type == null || type == typeof(void)) return null;
        if (type == typeof(Task)) return Task.CompletedTask;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
