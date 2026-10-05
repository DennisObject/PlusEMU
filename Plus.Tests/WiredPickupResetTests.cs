using System.Reflection;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

/// <summary>
/// A wired box that leaves the room for an inventory comes back with default settings; a room unloading keeps them.
/// </summary>
public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task PickingUpAWiredBoxForgetsItsSettings()
    {
        var store = ResettableStore();
        var (item, _) = PlaceBox(store, 40, 1);

        await Pickup(40);

        Assert.Equal([[40u]], store.Resets);
        Assert.Empty(store.Saved);
        Assert.Null(_room.GetRoomItemHandler().GetItem(40));
        Assert.False(_room.GetWired().TryGet(40, out _));
        Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
    }

    [Fact]
    public void EveryRemovalOfAPlacedBoxForgetsItsSettings()
    {
        // Ejecting, :ejectall and deleting the room all take the box through RemoveFurniture.
        var store = ResettableStore();
        PlaceBox(store, 41, 1, userId: 99);

        _room.GetRoomItemHandler().RemoveFurniture(null!, 41);

        Assert.Equal([[41u]], store.Resets);
        Assert.Empty(store.Saved);
        Assert.False(_room.GetWired().TryGet(41, out _));
    }

    [Fact]
    public void PickAllForgetsTheSettingsOfEveryOwnBoxAtOnce()
    {
        var store = ResettableStore();
        PlaceBox(store, 42, 1);
        PlaceBox(store, 43, 2);
        PlaceBox(store, 44, 3, userId: 99);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, Owned(Furni(45, InteractionType.None, WiredBoxType.None)), 1, 2, 0, true, false, false));

        _room.GetRoomItemHandler().RemoveItems(_client);

        Assert.Equal([[42u, 43u]], store.Resets);
        Assert.Equal([44u], store.Saved);
        Assert.False(_room.GetWired().TryGet(42, out _));
        Assert.False(_room.GetWired().TryGet(43, out _));
        Assert.True(_room.GetWired().TryGet(44, out _));
        Assert.NotNull(_room.GetRoomItemHandler().GetItem(44));
        Assert.All(new uint[] { 42, 43, 45 }, id => Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(id)));
    }

    [Fact]
    public void UnloadingTheRoomKeepsTheSettings()
    {
        var store = ResettableStore();
        PlaceBox(store, 46, 1);

        _room.GetRoomItemHandler().Dispose();

        Assert.Empty(store.Resets);
        Assert.Equal([46u], store.Saved);
        Assert.False(_room.GetWired().TryGet(46, out _));
    }

    [Fact]
    public void RemovingATemporaryBoxLeavesSavedSettingsAlone()
    {
        var store = ResettableStore();
        var item = _room.GetRoomItemHandler().PlaceTemporaryFloorItem(
            Furni(0, InteractionType.WiredEffect, WiredBoxType.EffectShowMessage).Definition, 7, 1, 1, 0)!;

        Assert.True(_room.GetRoomItemHandler().RemoveTemporaryFloorItem(item));
        _room.GetRoomItemHandler().RemoveItems(_client);

        Assert.Empty(store.Resets);
    }

    [Fact]
    public async Task ABoxWhoseSettingsCannotBeForgottenStaysPlaced()
    {
        var store = ResettableStore();
        var (_, box) = PlaceBox(store, 47, 1);
        store.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Pickup(47));

        Assert.Equal([47u], store.Saved);
        Assert.Same(box.Item, _room.GetRoomItemHandler().GetItem(47));
        Assert.True(_room.GetWired().TryGet(47, out var attached));
        Assert.Same(box, attached);
        Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(47));
    }

    [Fact]
    public void PickAllWhoseSettingsCannotBeForgottenMovesNothing()
    {
        var store = ResettableStore();
        PlaceBox(store, 48, 1);
        PlaceBox(store, 49, 2);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, Owned(Furni(50, InteractionType.None, WiredBoxType.None)), 3, 1, 0, true, false, false));
        store.Fail = true;

        Assert.Throws<InvalidOperationException>(() => _room.GetRoomItemHandler().RemoveItems(_client));

        Assert.Equal([48u, 49u], store.Saved.Order());
        Assert.All(new uint[] { 48, 49, 50 }, id => Assert.NotNull(_room.GetRoomItemHandler().GetItem(id)));
        Assert.True(_room.GetWired().TryGet(48, out _));
        Assert.True(_room.GetWired().TryGet(49, out _));
        Assert.All(new uint[] { 48, 49, 50 }, id => Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(id)));
    }

    [Fact]
    public async Task ASaveBeforePickupIsForgottenAndASaveAfterIsRefused()
    {
        var store = ResettableStore();
        var (item, box) = PlaceBox(store, 51, 1);
        store.Saved.Clear();
        Assert.True(_room.GetWired().PublishLegacy(box, _room.GetWired().GenerateNewBox(item)!, () => store.Saved.Add(51)));

        await Pickup(51);

        Assert.Empty(store.Saved);
        Assert.False(_room.GetWired().PublishLegacy(box, _room.GetWired().GenerateNewBox(item)!, () => store.Saved.Add(51)));
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task ASaveRacingThePickupWaitsAndIsRefused()
    {
        var store = ResettableStore();
        var (item, box) = PlaceBox(store, 52, 1);
        Task<bool>? save = null;
        var waitedForPickup = false;
        store.DuringReset = () =>
        {
            var candidate = _room.GetWired().GenerateNewBox(item)!;
            using var started = new ManualResetEventSlim();
            save = Task.Run(() =>
            {
                started.Set();
                return _room.GetWired().PublishLegacy(box, candidate, () => store.Saved.Add(52));
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            // The save is held off until the box has left the wired engine.
            waitedForPickup = !save.Wait(TimeSpan.FromMilliseconds(300));
        };

        await Pickup(52);

        Assert.True(waitedForPickup);
        Assert.False(await save!);
        Assert.Empty(store.Saved);
    }

    private ResettableConfigurationStore ResettableStore()
    {
        var store = new ResettableConfigurationStore();
        typeof(WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room.GetWired(), store);
        _client.GetHabbo().Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) };
        return store;
    }

    private (Item Item, IWiredItem Box) PlaceBox(ResettableConfigurationStore store, uint id, int x, int userId = 7)
    {
        var item = Owned(Furni(id, InteractionType.WiredEffect, WiredBoxType.EffectShowMessage), userId);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, x, 1, 0, true, false, false));
        var box = _room.GetWired().LoadWiredBox(item)!;
        Assert.NotNull(box);
        store.Saved.Add(id);
        return (item, box);
    }

    private static Item Owned(Item item, int userId = 7)
    {
        item.UserId = userId;
        return item;
    }

    private Task Pickup(uint id) =>
        new PickupObjectEvent(Proxy<IGameClientManager>((_, _) => null), Proxy<IQuestManager>((_, _) => null), _database)
            .Parse(_client, ClientPacket(0, (int)id));

    /// <summary>Saved settings per box, as the database would hold them.</summary>
    private sealed class ResettableConfigurationStore : IWiredConfigurationStore
    {
        public HashSet<uint> Saved { get; } = [];
        public List<uint[]> Resets { get; } = [];
        public bool Fail { get; set; }
        public Action? DuringReset { get; set; }
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => null;
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) => Saved.Add(itemId);
        public void Reset(IReadOnlyCollection<uint> itemIds)
        {
            if (Fail) throw new InvalidOperationException("The database is unavailable.");
            DuringReset?.Invoke();
            Resets.Add(itemIds.Order().ToArray());
            Saved.ExceptWith(itemIds);
        }
    }
}
