using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Core.Settings;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffects;
using Plus.HabboHotel.Catalog.Clothing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Clothing;
using Plus.HabboHotel.Users.Clothing.Parts;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class ItemRedemptionServiceTests
{
    [Fact]
    public void CreditDenialsDoNotDeleteOrPublish()
    {
        var (room, client, sent, item) = Context(InteractionType.Exchange, 10);
        var store = new Store(); var service = Service(store);
        item.OwnerId = 99; service.RedeemCredits(room, client, item.Id);
        item.OwnerId = 1; item.Definition.InteractionType = InteractionType.Gate; service.RedeemCredits(room, client, item.Id);
        var (temporaryRoom, temporaryClient, _, temporaryItem) = Context(InteractionType.Exchange, 10, temporary: true);
        service.RedeemCredits(temporaryRoom, temporaryClient, temporaryItem.Id);
        item.Definition.InteractionType = InteractionType.Exchange; client.GetHabbo().Credits = int.MaxValue; service.RedeemCredits(room, client, item.Id);
        typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client.GetHabbo(), true);
        client.GetHabbo().Credits = 0; service.RedeemCredits(room, client, item.Id);
        Assert.Equal(0, store.ExchangeDeletes); Assert.Empty(sent); Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id));
    }

    [Fact]
    public void CreditDeleteFailureLeavesWalletFurnitureAndPacketsUntouched()
    {
        var (room, client, sent, item) = Context(InteractionType.Exchange, 10);
        var store = new Store { Fail = true };
        Assert.Throws<InvalidOperationException>(() => Service(store).RedeemCredits(room, client, item.Id));
        Assert.Equal(0, client.GetHabbo().Credits); Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id)); Assert.Empty(sent);
    }

    [Fact]
    public void CreditOwnerSuccessPublishesAfterExactDelete()
    {
        var (room, client, sent, item) = Context(InteractionType.Exchange, 10);
        var store = new Store(() => { Assert.Equal(0, client.GetHabbo().Credits); Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id)); Assert.Empty(sent); });
        Service(store).RedeemCredits(room, client, item.Id);
        Assert.Equal(10, client.GetHabbo().Credits); Assert.Equal(1, store.ExchangeDeletes); Assert.NotEmpty(sent);
    }

    [Fact]
    public void ClothingTransactionFailurePublishesNothing()
    {
        var (room, client, sent, item) = Context(InteractionType.PurchasableClothing, 12);
        var store = new Store { Fail = true };
        Assert.Throws<InvalidOperationException>(() => Service(store, new ClothingItem(12, "shirt", "10,11")).RedeemClothing(client, item.Id));
        Assert.Empty(client.GetHabbo().Clothing.GetClothingParts); Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id)); Assert.Empty(sent);
    }

    [Fact]
    public void ClothingSuccessPublishesCommittedPartsAfterStoreReturns()
    {
        var (room, client, sent, item) = Context(InteractionType.PurchasableClothing, 12);
        var store = new Store(() =>
        {
            Assert.Empty(client.GetHabbo().Clothing.GetClothingParts);
            Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id));
            Assert.Empty(sent);
        });

        Service(store, new ClothingItem(12, "shirt", "10,11")).RedeemClothing(client, item.Id);

        Assert.Equal(new[] { 10, 11 }, client.GetHabbo().Clothing.GetClothingParts.Select(part => part.PartId).OrderBy(id => id));
        Assert.Null(room.GetRoomItemHandler().GetItem(item.Id));
        Assert.NotEmpty(sent);
    }

    [Fact]
    public void FigureSetComposerIsStableAfterSourceMutation()
    {
        var (_, client, sent, _) = Context(InteractionType.Exchange, 10);
        var source = new List<ClothingParts> { new(1, 10, "shirt") };
        var composer = new FigureSetIdsComposer(source);
        client.Send(composer);
        source[0].PartId = 99;
        source[0].Part = "changed";
        source.Add(new(2, 20, "trousers"));
        client.Send(composer);

        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0].Payload, sent[1].Payload);
    }

    private static ItemRedemptionService Service(Store store, ClothingItem? clothing = null) => new(store, Settings(), new Clothing(clothing));
    private static ISettingsManager Settings()
    {
        var proxy = DispatchProxy.Create<ISettingsManager, SettingsProxy>(); return proxy;
    }
    public class SettingsProxy : DispatchProxy { protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "TryGetValue" ? "1" : null; }
    private sealed class Clothing(ClothingItem? item) : IClothingManager
    {
        public ICollection<ClothingItem> GetClothingAllParts => item == null ? [] : [item]; public void Init() { }
        public bool TryGetClothing(int itemId, out ClothingItem clothing) { clothing = item!; return item?.Id == itemId; }
    }
    private sealed class Store(Action? before = null) : IItemRedemptionStore
    {
        public bool Fail; public int ExchangeDeletes;
        public void DeleteExchange(uint itemId, int ownerId, uint roomId) { before?.Invoke(); ExchangeDeletes++; if (Fail) throw new InvalidOperationException("forced"); }
        public IReadOnlyList<ClothingParts> ConsumeClothing(uint itemId, int ownerId, uint roomId, string name, IReadOnlyCollection<int> ids) { before?.Invoke(); if (Fail) throw new InvalidOperationException("forced"); return ids.Select(id => new ClothingParts(id, id, name)).ToArray(); }
    }

    private static (Room Room, Plus.HabboHotel.GameClients.GameClient Client, List<(uint Header, byte[] Payload)> Sent, Item Item) Context(InteractionType type, int value, bool temporary = false)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 9; room.OwnerName = "owner"; room.Type = "private"; room.UsersWithRights = [];
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new Gamemap(room, new RoomModel("test", 0, 0, 0, 0, "00\r00", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty));
        var item = new Item { Id = 7, RoomId = 9, OwnerId = 1, IsTemporary = temporary, Definition = new() { Type = ItemType.Wall, InteractionType = type, BehaviourData = value } };
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, room);
        var walls = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_wallItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!; walls[item.Id] = item;
        var inventoryItem = new InventoryItem { Id = item.Id, OwnerId = 1, Definition = item.Definition };
        var habbo = new Habbo { Id = 1, Username = "owner", CurrentRoom = room, Clothing = new(), Inventory = new InventoryComponent { Furniture = new([], [inventoryItem]) } };
        habbo.Clothing.Init(habbo); var (client, sent) = HabbiconTestSupport.Client(habbo); return (room, client, sent, item);
    }
}
