using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items.Wired.Chests;

// All calls enter through WiredComponent's room engine. WalletSync joins Plus's live wallet seam.
public sealed class WiredChestRoom(Room room, IWiredChestStore store, TimeProvider clock, Action<WiredRuntimeEvent> publish)
{
    private readonly Dictionary<int, Session> _sessions = [];
    private readonly Dictionary<int, uint> _viewers = [];
    public bool HasPending => _sessions.Count > 0;
    public IWiredChestStore Store => store;

    public WiredChestSnapshot? Read(Item item) => item.IsTemporary || item.RoomId != room.Id
        || !ReferenceEquals(room.GetRoomItemHandler().GetItem(item.Id), item) ? null : store.Load(item);
    public WiredChestSnapshot[] Read(IEnumerable<Item> items) => items.Select(Read).OfType<WiredChestSnapshot>().ToArray();

    public bool Start(GameClient client, uint sourceId, Item[] chests, WiredChestContract? contract, int mode = 0, int multiplier = 1, int timeout = 0)
    {
        var habbo = client.GetHabbo();
        var actor = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);

        if (actor == null || actor.IsBot || !ReferenceEquals(habbo.CurrentRoom, room)) {
            return false;
        }

        var inventory = habbo.Inventory;

        if (inventory == null) {
            if (_sessions.TryGetValue(habbo.Id, out var invalid)) {
                End(invalid, WiredChestFailure.Invalid);
            }
            else {
                Fail(actor, sourceId, WiredChestFailure.Invalid);
            }

            return false;
        }

        if (_sessions.TryGetValue(habbo.Id, out var previous)) {
            if (previous.CancelAt == null) {
                Fail(actor, sourceId, WiredChestFailure.AlreadyTrading);

                return false;
            }

            End(previous, WiredChestFailure.TradeCancelled, notify: false);
        }
        else if (actor.IsTrading) {
            Fail(actor, sourceId, WiredChestFailure.AlreadyTrading);

            return false;
        }

        var snapshots = Read(chests);
        var failure = snapshots.Length != chests.Length || snapshots.Length == 0 ? WiredChestFailure.ChestNotInRoom
            : contract != null && snapshots.Any(chest => !chest.Usable) ? WiredChestFailure.NoOrLockedChests
            : contract == null && snapshots.Any(chest => !chest.CanDeposit((uint)habbo.Id, room.GetWired().Settings.CanModify(client))) ? WiredChestFailure.NoOrLockedChests
            : (WiredChestFailure?)null;

        if (failure != null) {
            Fail(actor, sourceId, failure.Value);

            return false;
        }

        if (contract is { Kind: WiredContractKind.Reward }) {
            var result = Move(client, new()
            {
                RoomId = room.Id,
                UserId = habbo.Id,
                SourceId = sourceId,
                ChestIds = chests.Select(item => item.Id).ToArray(),
                Reward = WiredChestContracts.Scale(contract.Reward, multiplier),
                Wired = true,
                Multiplier = multiplier
            });

            if (result.Succeeded && !result.Replayed) {
                client.Send(new WiredChestRewardComposer(result, contract.RewardText, contract.ShowDialog));
            }

            Outcome(actor, sourceId, result);

            return result.Succeeded;
        }

        var session = new Session(client, actor, sourceId, chests.Select(item => item.Id).ToArray(), contract, mode, multiplier,
            timeout > 0 ? clock.GetUtcNow().AddSeconds(timeout) : null);
        _sessions[habbo.Id] = session;
        actor.IsTrading = true;
        client.Send(new WiredChestTradeOpenComposer(contract, snapshots[0].Kind, mode, multiplier, timeout, previous != null));
        SendOffer(session);

        return true;
    }

    public long? ReadTransaction(RoomUser actor, string name)
    {
        if (!_sessions.TryGetValue(actor.HabboId, out var session) || !ReferenceEquals(session.Actor, actor)) {
            return null;
        }

        if (!TryOffered(session, out var offered)) {
            return null;
        }

        var times = session.Contract == null ? offered.Length > 0 ? 1 : 0
            : WiredChestContracts.Times(session.Contract, offered, session.Mode, session.Multiplier);
        var canAccept = times > 0 && !session.Accepted && session.CancelAt == null && !(session.Deadline <= clock.GetUtcNow());

        return name switch
        {
            "@transaction.in_trade" => 1,
            "@transaction.contract_id" => session.Contract?.Id,
            "@transaction.state" => session.Accepted ? 2 : 1,
            "@transaction.can_accept" => canAccept ? 1 : null,
            "@transaction.current_multiplier" => times,
            _ => null
        };
    }

    public void Offer(GameClient client, uint[] ids, bool add)
    {
        if (!_sessions.TryGetValue(client.GetHabbo().Id, out var session)) {
            return;
        }

        if (!TryInventory(session, out var inventory)) {
            return;
        }

        if (!add) {
            foreach (var id in ids) {
                session.Offer.Remove(id);
            }
        }
        else {
            var wanted = ids.Distinct().Where(id => !session.Offer.Contains(id)).ToArray();

            if (wanted.Length + session.Offer.Count > 500 || wanted.Any(id => inventory.GetItem(id) is not { } item
                || item.OwnerId != client.GetHabbo().Id || !item.Definition.AllowTrade
                || WiredChestFurniture.IsChest(item.Definition) || WiredChestFurniture.IsContract(item.Definition)
                || !WiredChestContracts.CanOffer(session.Contract, item))) {
                return;
            }

            session.Offer.UnionWith(wanted);
        }

        session.Accepted = false;
        SendOffer(session);
    }

    public void Confirm(GameClient client, bool final)
    {
        if (!_sessions.TryGetValue(client.GetHabbo().Id, out var session)) {
            return;
        }

        if (!TryOffered(session, out var offered)) {
            return;
        }

        var times = session.Contract == null ? offered.Length > 0 ? 1 : 0
            : WiredChestContracts.Times(session.Contract, offered, session.Mode, session.Multiplier);

        if (times == 0 || session.CancelAt != null || session.Deadline <= clock.GetUtcNow()
            || !ReferenceEquals(session.Actor, room.GetRoomUserManager().GetRoomUserByHabbo(client.GetHabbo().Id))
            || !ReferenceEquals(client.GetHabbo().CurrentRoom, room)) {
            return;
        }

        if (!final) {
            session.Accepted = true;
            SendOffer(session);

            return;
        }

        if (!session.Accepted) {
            return;
        }

        // Consume the session before I/O. Duplicate confirmations can never execute twice.
        _sessions.Remove(client.GetHabbo().Id);
        session.Actor.IsTrading = false;
        var result = Move(client, new()
        {
            OperationId = session.OperationId,
            RoomId = room.Id,
            UserId = client.GetHabbo().Id,
            SourceId = session.SourceId,
            ChestIds = session.Chests,
            PaymentIds = session.Contract == null ? session.Offer.ToArray()
                : WiredChestContracts.Payment(session.Contract, offered, session.Mode, session.Multiplier),
            Reward = session.Contract is { Kind: WiredContractKind.Trade } trade ? WiredChestContracts.Scale(trade.Reward, times) : [],
            Wired = session.Contract != null,
            CanModify = room.GetWired().Settings.CanModify(client),
            Multiplier = times
        });
        client.Send(new WiredChestTradeEndComposer(result.Failure));

        if (session.Contract != null) {
            Outcome(session.Actor, session.SourceId, result, notifyFailure: false);
        }

        RefreshChests(session.Chests);
    }

    public void Cancel(GameClient client, WiredChestFailure reason = WiredChestFailure.UserCancelled)
    {
        if (_sessions.TryGetValue(client.GetHabbo().Id, out var session)) {
            End(session, reason);
        }
    }

    public bool CancelByWired(IEnumerable<RoomUser> actors, uint[] contracts, bool any)
    {
        var changed = false;

        foreach (var actor in actors) {
            if (_sessions.TryGetValue(actor.HabboId, out var session) && (any || contracts.Contains(session.SourceId))) {
                session.CancelAt = clock.GetUtcNow().AddMilliseconds(500);
                changed = true;
            }
        }

        return changed;
    }

    public void Poll()
    {
        var now = clock.GetUtcNow();

        foreach (var session in _sessions.Values.ToArray()) {
            if (!TryInventory(session, out _)) {
                continue;
            }

            if (session.Deadline <= now || session.CancelAt <= now || !ReferenceEquals(session.Client.GetHabbo()?.CurrentRoom, room)) {
                End(session, session.Deadline <= now ? WiredChestFailure.Timeout : WiredChestFailure.TradeCancelled);
            }
        }
    }

    public void Leave(RoomUser actor)
    {
        if (_sessions.TryGetValue(actor.HabboId, out var session)) {
            End(session, WiredChestFailure.TradeCancelled);
        }

        if (_viewers.Remove(actor.HabboId, out var viewed) && room.GetRoomItemHandler().GetItem(viewed) is { } viewedItem) {
            Refresh(viewedItem);
        }

        foreach (var item in room.GetRoomItemHandler().GetFloor.Where(item => item.OwnerId == actor.HabboId && WiredChestFurniture.IsChest(item.Definition))) {
            var chest = Read(item);

            if (chest is { Settings.AutoLock: true, Settings.Locked: false }) {
                store.SaveSettings(item, actor.HabboId, false, false, old => old with { Locked = true });
                Refresh(item);
            }
        }
    }

    public WiredChestTransferResult Move(GameClient client, WiredChestTransfer request)
    {
        var habbo = client.GetHabbo();
        var reserved = new List<InventoryItem>();
        WiredChestTransferResult result;

        lock (habbo.InventoryMutationSync)
            lock (habbo.WalletSync) {
                if (habbo.Inventory is not { } inventory || habbo.WalletClosed || request.UserId != habbo.Id || request.RoomId != room.Id
                    || !ReferenceEquals(habbo.Client, client) || !ReferenceEquals(habbo.CurrentRoom, room)) {
                    return WiredChestTransferResult.Refused(WiredChestFailure.Invalid);
                }

                try {
                    foreach (var id in request.PaymentIds) {
                        var item = inventory.Furniture.GetItem(id);

                        if (item == null || item.OwnerId != habbo.Id || !item.TryReserve()) {
                            return WiredChestTransferResult.Refused(WiredChestFailure.Invalid);
                        }

                        reserved.Add(item);
                    }

                    result = store.Transfer(request with { LiveCredits = habbo.Credits });

                    if (!result.Succeeded || result.Replayed) {
                        return result;
                    }

                    habbo.Credits = result.Credits;

                    foreach (var id in result.Removed) {
                        inventory.Furniture.RemoveItem(id);
                        client.Send(new FurniListRemoveComposer(id));
                    }

                    foreach (var item in result.Received) {
                        inventory.Furniture.AddItem(item);
                        client.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(item)));
                    }

                    client.Send(new CreditBalanceComposer(habbo.Credits));
                }
                catch (Exception error) when (error is System.Data.Common.DbException or System.Data.DBConcurrencyException) {
                    result = WiredChestTransferResult.Refused(WiredChestFailure.DatabaseError);
                }
                finally {
                    foreach (var item in reserved) {
                        item.ReleaseReservation();
                    }
                }
            }

        RefreshChests(request.ChestIds);

        return result;
    }

    public void Upgrade(GameClient client, Item item, int count)
    {
        var habbo = client.GetHabbo();
        WiredChestUpgradeResult result;

        lock (habbo.WalletSync) {
            if (habbo.WalletClosed || !ReferenceEquals(habbo.CurrentRoom, room) || !ReferenceEquals(habbo.Client, client)) {
                return;
            }

            try {
                result = store.Upgrade(item, habbo.Id, count, habbo.Credits, habbo.Diamonds);

                if (result.Code == 0) {
                    habbo.Credits = result.Credits;
                    habbo.Diamonds = result.Diamonds;
                    client.Send(new CreditBalanceComposer(habbo.Credits));
                    client.Send(new Plus.Communication.Packets.Outgoing.Notifications.ActivityPointsComposer(habbo.Currencies));
                }
            }
            catch (System.Data.Common.DbException) {
                result = new(4);
            }
        }

        client.Send(new WiredChestUpgradeComposer(item.Id, result.Code));
        Open(client, item);
    }

    public void Open(GameClient client, Item item)
    {
        var chest = Read(item);

        if (chest == null || !chest.CanOpen((uint)client.GetHabbo().Id, room.GetWired().Settings.CanInspect(client))) {
            return;
        }

        if (_viewers.TryGetValue(client.GetHabbo().Id, out var old) && old != item.Id && room.GetRoomItemHandler().GetItem(old) is { } prior) {
            _viewers.Remove(client.GetHabbo().Id);
            Refresh(prior);
        }

        _viewers[client.GetHabbo().Id] = item.Id;
        var owner = chest.OwnerId == client.GetHabbo().Id;
        var modify = room.GetWired().Settings.CanModify(client);
        client.Send(new WiredChestContentsComposer(chest, owner, item.Definition.SpriteId,
            item.Definition.ItemName.Contains("_starter", StringComparison.Ordinal),
            chest.CanWithdraw((uint)client.GetHabbo().Id, modify), chest.CanDeposit((uint)client.GetHabbo().Id, modify),
            owner || !chest.Settings.Locked && (room.OwnerId == client.GetHabbo().Id || chest.Settings.WiredEnabled && modify)));

        if (chest.Kind == WiredChestKind.Furni) {
            var pages = Math.Max(1, (chest.Items.Length + 499) / 500);

            for (var page = 0; page < pages; page++) {
                client.Send(new WiredChestFurniChunkComposer(chest.Id, pages, page, chest.Items.Skip(page * 500).Take(500).ToArray(), chest.DepositTransactions));
            }
        }

        Refresh(item);
    }

    public void Close(GameClient client, Item item)
    {
        if (_viewers.GetValueOrDefault(client.GetHabbo().Id) == item.Id) {
            _viewers.Remove(client.GetHabbo().Id);
            Refresh(item);
        }
    }

    public void Refresh(Item item)
    {
        var chest = Read(item);

        if (chest == null) {
            return;
        }

        var s = chest.Settings;
        var map = new Dictionary<string, string>
        {
            ["state"] = s.StateMode == 3 && item.ExtraData is MapDataFormat existing
                ? existing.Data.GetValueOrDefault("state", "0")
                : (s.StateMode == 1 || s.StateMode == 0 && _viewers.Values.Contains(item.Id) ? 1 : 0).ToString(),
            ["locked"] = Flag(s.Locked),
            ["auto_lock"] = Flag(s.AutoLock),
            ["capacity"] = s.Capacity.ToString(),
            ["contents_count"] = chest.Amount.ToString(),
            ["capacity_level"] = chest.Level.ToString(),
            ["chest_name"] = s.Name,
            ["chest_desc"] = s.Description,
            ["everyone_can_open"] = Flag(s.EveryoneCanOpen),
            ["everyone_can_donate"] = Flag(s.EveryoneCanDonate),
            ["is_wired_enabled"] = Flag(s.WiredEnabled),
            ["state_control_mode"] = s.StateMode.ToString(),
            ["preview_mode"] = s.PreviewMode.ToString(),
            ["preview_amount"] = s.PreviewAmount.ToString(),
            ["visuals"] = string.Join(';', Preview(chest).Select(type => $"{(type.Wall ? "true" : "false")},{type.SpriteId}"))
        };
        item.ExtraData = new MapDataFormat(map);
        item.UpdateState();
    }

    public static WiredChestItemType[] Preview(WiredChestSnapshot chest)
    {
        IEnumerable<InventoryItem> source = chest.Settings.PreviewMode switch
        {
            1 or 2 => chest.Items.OrderBy(_ => Random.Shared.Next()),
            7 => chest.Items.OrderBy(item => chest.RandomKeys.GetValueOrDefault(item.Id)).ThenBy(item => item.Id),
            3 or 4 => chest.Items.Reverse(),
            5 or 6 => chest.Items,
            _ => []
        };
        var types = source.Select(WiredChestItemType.Of);

        if (chest.Settings.PreviewMode is 2 or 4 or 6) {
            types = types.Distinct();
        }

        return types.Take(chest.Settings.PreviewAmount).ToArray();
    }
    private static string Flag(bool value) => value ? "1" : "0";
    private void RefreshChests(IEnumerable<uint> ids)
    {
        foreach (var id in ids) {
            if (room.GetRoomItemHandler().GetItem(id) is { } item) {
                Refresh(item);
            }
        }
    }
    private bool TryInventory(Session session, out FurnitureInventoryComponent inventory)
    {
        var loaded = session.Client.GetHabbo().Inventory;

        if (loaded == null) {
            inventory = null!;
            End(session, WiredChestFailure.Invalid);

            return false;
        }

        inventory = loaded.Furniture;

        return true;
    }
    private bool TryOffered(Session session, out InventoryItem[] offered)
    {
        if (!TryInventory(session, out var inventory)) {
            offered = [];

            return false;
        }

        offered = session.Offer.Select(inventory.GetItem).OfType<InventoryItem>().ToArray();

        return true;
    }
    private void SendOffer(Session session)
    {
        if (!TryOffered(session, out var offered)) {
            return;
        }

        session.Client.Send(new WiredChestTradeItemsComposer(session.Client.GetHabbo().Id, offered,
            session.Contract, session.Mode, session.Multiplier, session.Accepted,
            session.Deadline is { } end ? Math.Max(0, (int)Math.Ceiling((end - clock.GetUtcNow()).TotalSeconds)) : 0));
    }
    private void End(Session session, WiredChestFailure reason, bool notify = true)
    {
        _sessions.Remove(session.Client.GetHabbo().Id);
        session.Actor.IsTrading = false;

        if (notify) {
            session.Client.Send(new WiredChestTradeEndComposer(reason));
        }

        if (session.Contract != null) {
            Fail(session.Actor, session.SourceId, reason, notify: false);
        }
    }
    public void Outcome(RoomUser actor, uint sourceId, WiredChestTransferResult result, bool notifyFailure = true)
    {
        if (result.Succeeded) {
            publish(new(WiredEventKind.TransactionComplete) { Actor = actor, EventItem = room.GetRoomItemHandler().GetItem(sourceId), Transaction = result.Figures });
        }
        else {
            Fail(actor, sourceId, result.Failure!.Value, notifyFailure);
        }
    }
    public void Fail(RoomUser actor, uint sourceId, WiredChestFailure reason, bool notify = true)
    {
        if (notify) {
            actor.GetClient()?.Send(new WiredChestTradeEndComposer(reason));
        }

        publish(new(WiredEventKind.TransactionFail) { Actor = actor, EventItem = room.GetRoomItemHandler().GetItem(sourceId), Code = (int)reason });
    }
    public void Lock(GameClient client, bool locked, bool all)
    {
        var affected = 0;
        var userId = client.GetHabbo().Id;
        var every = locked && all;

        if (every && room.OwnerId != userId) {
            client.Send(new WiredChestLockComposer(locked, all, 0));

            return;
        }

        foreach (var item in room.GetRoomItemHandler().GetFloor.Where(item => WiredChestFurniture.IsChest(item.Definition))) {
            if (Read(item) is not { } chest || chest.Settings.Locked == locked || !every && chest.OwnerId != userId) {
                continue;
            }

            if (store.SaveSettings(item, userId, room.GetWired().Settings.CanModify(client), room.OwnerId == userId, old => old with { Locked = locked })) {
                affected++;
                Refresh(item);
            }
        }

        client.Send(new WiredChestLockComposer(locked, all, affected));
    }
    private sealed class Session(GameClient client, RoomUser actor, uint sourceId, uint[] chests, WiredChestContract? contract, int mode, int multiplier, DateTimeOffset? deadline)
    {
        public Guid OperationId { get; } = Guid.NewGuid();
        public GameClient Client { get; } = client;
        public RoomUser Actor { get; } = actor;
        public uint SourceId { get; } = sourceId;
        public uint[] Chests { get; } = chests;
        public WiredChestContract? Contract { get; } = contract;
        public int Mode { get; } = mode;
        public int Multiplier { get; } = multiplier;
        public DateTimeOffset? Deadline { get; } = deadline;
        public DateTimeOffset? CancelAt { get; set; }
        public bool Accepted { get; set; }
        public HashSet<uint> Offer { get; } = [];
    }
}
