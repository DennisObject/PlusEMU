using System.Collections.Immutable;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Recycler;

[Singleton]
public interface IRecyclerService
{
    void GetStatus(GameClient session);
    void GetPrizes(GameClient session);
    void Recycle(GameClient session, IReadOnlyList<uint> itemIds);
    bool TryOpen(GameClient session, uint itemId);
}

public sealed class RecyclerService(IRecyclerStore store, IItemDataManager definitions, TimeProvider clock,
    IRecyclerRandom random, ILogger<RecyclerService> logger) : IRecyclerService
{
    public void GetStatus(GameClient session)
    {
        var configuration = Configuration();
        var items = definitions.Items;
        if (!Valid(configuration, items) || session.GetHabbo() is not { } habbo) {
            session.Send(new RecyclerStatusComposer(2, 0));
            return;
        }
        DateTimeOffset? next;
        try {
            next = store.NextAllowed(habbo.Id);
        }
        catch (DbException exception) {
            logger.LogError(exception, "Could not read recycler cooldown for user {UserId}", habbo.Id);
            session.Send(new RecyclerStatusComposer(2, 0));
            return;
        }
        var remaining = next is null ? 0 : Math.Max(0, Math.Ceiling((next.Value - clock.GetUtcNow()).TotalSeconds));
        session.Send(new RecyclerStatusComposer(remaining > 0 ? 3 : 1, (int)Math.Min(int.MaxValue, remaining)));
    }

    public void GetPrizes(GameClient session)
    {
        var configuration = Configuration();
        var items = definitions.Items;
        session.Send(new RecyclerPrizesComposer(Valid(configuration, items)
            ? configuration.Levels.Select(level => new RecyclerPrizeList(level.Id, level.Chance,
                level.Prizes.Select(prize => items[prize.ItemId]).Select(definition =>
                    new RecyclerProduct(definition.ItemName, definition.ProductType, definition.SpriteId)).ToImmutableArray())).ToImmutableArray()
            : []));
    }

    public void Recycle(GameClient session, IReadOnlyList<uint> itemIds)
    {
        if (session.GetHabbo() is not { } habbo) {
            return;
        }
        lock (habbo.WalletSync) {
            var room = habbo.CurrentRoom;
            var configuration = Configuration();
            var items = definitions.Items;
            var box = RecyclerBox.Find(items.Values);
            if (!Active(session, habbo) || room is null || room.Type != "private" || !Valid(configuration, items) || box is null
                || !Admitted(session, habbo, room)
                || itemIds.Count != configuration.Slots || itemIds.Distinct().Count() != itemIds.Count || itemIds.Any(id => id == 0)) {
                session.Send(new RecyclerFinishedComposer(2, 0));
                return;
            }
            var selected = itemIds.Select(habbo.Inventory.Furniture.GetItem).ToArray();
            if (selected.Any(item => item is null || item.OwnerId != (uint)habbo.Id || !item.Definition.AllowEcotronRecycle
                || item.UniqueNumber != 0 || item.UniqueSeries != 0)) {
                session.Send(new RecyclerFinishedComposer(2, 0));
                return;
            }
            var reserved = new List<InventoryItem>(selected.Length);
            try {
                foreach (var item in selected.Select(item => item!).OrderBy(item => item.Id)) {
                    if (!item.TryReserve()) {
                        session.Send(new RecyclerFinishedComposer(2, 0));
                        return;
                    }
                    reserved.Add(item);
                    if (!ReferenceEquals(habbo.Inventory.Furniture.GetItem(item.Id), item)) {
                        session.Send(new RecyclerFinishedComposer(2, 0));
                        return;
                    }
                }
                var prize = Pick(configuration);
                RecycledBox? committed;
                try {
                    committed = store.Recycle(habbo.Id, box.Id, configuration, prize,
                        reserved.Select(item => new RecyclerInput(item.Id, item.Definition.Id)).ToArray(), clock.GetUtcNow());
                }
                catch (DbException exception) {
                    logger.LogError(exception, "Could not recycle furniture for user {UserId}", habbo.Id);
                    session.Send(new RecyclerFinishedComposer(2, 0));
                    return;
                }
                if (committed is null) {
                    session.Send(new RecyclerFinishedComposer(2, 0));
                    return;
                }
                var reward = new InventoryItem { Id = committed.Id, OwnerId = (uint)habbo.Id, Definition = box,
                    ExtraData = FurniExtraData.Load(box, committed.ExtraData, true) };
                foreach (var item in reserved) {
                    habbo.Inventory.Furniture.RemoveItem(item.Id);
                }
                if (!habbo.Inventory.Furniture.AddItem(reward)) {
                    throw new InvalidOperationException("Committed recycler box was already present in inventory.");
                }
                var snapshot = InventoryItemSnapshot.Capture(reward);
                foreach (var item in reserved) {
                    session.Send(new FurniListRemoveComposer(item.Id));
                }
                session.Send(new FurniListAddComposer(snapshot));
                session.Send(new FurniListNotificationComposer(reward.Id, 1));
                session.Send(new RecyclerFinishedComposer(1, 0));
                session.Send(new FurniListUpdateComposer());
            }
            finally {
                foreach (var item in reserved) {
                    item.ReleaseReservation();
                }
            }
        }
    }

    public bool TryOpen(GameClient session, uint itemId)
    {
        var habbo = session.GetHabbo();
        var room = habbo?.CurrentRoom;
        var handler = room?.GetRoomItemHandler();
        var item = handler?.GetItem(itemId);
        if (item is null || !RecyclerBox.IsIdentity(item.Definition)) {
            return false;
        }
        var actor = room!.GetRoomUserManager()?.GetRoomUserByHabbo(habbo!.Id);
        if (actor is null || !ReferenceEquals(actor.GetClient(), session)) {
            return true;
        }
        var placement = item.Placement;
        if (room.UsesV2Movement) {
            room.GetGameMap().Navigation!.RunOwner(actor, (_, _) => Open(session, habbo!, room, handler!, item, placement));
        }
        else {
            Open(session, habbo!, room, handler!, item, placement);
        }
        return true;
    }

    private void Open(GameClient session, Habbo habbo, Room room, RoomItemHandling handler, Item item, long placement)
    {
        lock (item) {
            InventoryItemSnapshot reward;
            OpenGiftWireData opened;
            lock (habbo.WalletSync) {
                var items = definitions.Items;
                var box = RecyclerBox.Find(items.Values);
                if (!Active(session, habbo) || !ReferenceEquals(habbo.CurrentRoom, room) || room.Type != "private"
                    || !Admitted(session, habbo, room)
                    || !room.CheckRights(session, false, true) || box is null || item.Definition.Id != box.Id || item.IsTemporary
                    || item.OwnerId != (uint)habbo.Id || item.RoomId != room.Id || item.Placement != placement
                    || !ReferenceEquals(item.GetRoom(), room) || !ReferenceEquals(handler.GetItem(item.Id), item)
                    || !DateOnly.TryParseExact(item.LegacyDataString, "d-M-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
                    return;
                }
                GiftContent? content;
                uint? rewardId;
                try {
                    content = store.FindBox(item.Id, habbo.Id, room.Id, box.Id);
                    if (content is null || !items.TryGetValue(content.BaseId, out var definition)
                        || !ValidProduct(definition)) {
                        return;
                    }
                    // Prepare the data before the transaction; malformed contents must never consume the box.
                    var data = FurniExtraData.Load(definition, content.ExtraData, true);
                    var serialized = data.Serialize();
                    rewardId = store.OpenBox(item.Id, habbo.Id, room.Id, box.Id, content);
                    if (rewardId is null) {
                        return;
                    }
                    var inventory = new InventoryItem { Id = rewardId.Value, OwnerId = (uint)habbo.Id, Definition = definition, ExtraData = data };
                    if (!habbo.Inventory.Furniture.AddItem(inventory)) {
                        throw new InvalidOperationException("Committed recycler reward was already present in inventory.");
                    }
                    reward = InventoryItemSnapshot.Capture(inventory);
                    opened = new(definition.ProductType, definition.SpriteId, definition.ItemName, inventory.Id, false, serialized);
                }
                catch (DbException exception) {
                    logger.LogError(exception, "Could not open recycler box {ItemId} for user {UserId}", item.Id, habbo.Id);
                    return;
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException) {
                    logger.LogWarning(exception, "Recycler box {ItemId} has malformed contents", item.Id);
                    return;
                }
            }
            // Owner routing precedes WalletSync. Room callbacks and Wired never run while that monitor is held.
            // A concurrent unload may already have detached this exact instance; never touch a replacement by id.
            if (ReferenceEquals(room.GetRoomItemHandler(), handler) && ReferenceEquals(item.GetRoom(), room)
                && ReferenceEquals(handler.GetItem(item.Id), item)) {
                handler.RemoveFurniture(session, item);
            }
            session.Send(new FurniListAddComposer(reward));
            session.Send(new FurniListNotificationComposer(reward.Id, opened.Type == "s" ? 1 : 2));
            session.Send(new OpenGiftComposer(opened));
            session.Send(new FurniListUpdateComposer());
        }
    }

    internal RecyclerPrize Pick(RecyclerConfiguration configuration)
    {
        // The reference rolls each rarer level in descending order, then chooses uniformly within it.
        var level = configuration.Levels.OrderByDescending(level => level.Id)
            .First(level => level.Id == 1 || random.Next(level.Chance) == level.Chance - 1);
        return level.Prizes[random.Next(level.Prizes.Length)];
    }

    private RecyclerConfiguration Configuration()
    {
        try {
            return store.Load();
        }
        catch (DbException exception) {
            logger.LogError(exception, "Could not load recycler configuration");
            return RecyclerConfiguration.Closed;
        }
    }
    private static bool Valid(RecyclerConfiguration configuration, IReadOnlyDictionary<uint, ItemDefinition> items) => configuration.Enabled && configuration.Slots is >= 1 and <= 12
        && configuration.CooldownSeconds is >= 0 and <= 86400 && RecyclerBox.Find(items.Values) is not null
        && configuration.Levels.Length is >= 1 and <= 5 && configuration.Levels.Any(level => level.Id == 1 && level.Chance == 1)
        && configuration.Levels.Select(level => level.Id).Distinct().Count() == configuration.Levels.Length
        && configuration.Levels.All(level => level.Id is >= 1 and <= 5 && level.Chance > 0 && level.Prizes.Length is >= 1 and <= 500
            && level.Prizes.All(prize => items.TryGetValue(prize.ItemId, out var definition)
                && ValidProduct(definition)))
        && configuration.Levels.SelectMany(level => level.Prizes).Select(prize => prize.Id).Distinct().Count()
            == configuration.Levels.Sum(level => level.Prizes.Length);
    private static bool ValidProduct(ItemDefinition definition) => definition.SpriteId > 0
        && (definition.ProductType == "s" && definition.Type == ItemType.Floor
            || definition.ProductType == "i" && definition.Type == ItemType.Wall);
    private static bool Admitted(GameClient session, Habbo habbo, Room room) =>
        room.GetRoomUserManager()?.GetRoomUserByHabbo(habbo.Id) is { IsTrading: false } actor
        && ReferenceEquals(actor.GetClient(), session);
    private static bool Active(GameClient session, Habbo habbo) => !habbo.WalletClosed
        && ReferenceEquals(session.GetHabbo(), habbo) && ReferenceEquals(habbo.Client, session);
}
