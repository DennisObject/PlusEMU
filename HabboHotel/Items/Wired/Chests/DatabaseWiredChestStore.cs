using System.Data;
using System.Text.Json;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Chests;

public sealed class DatabaseWiredChestStore(IDatabase database, IItemDataManager definitions) : IWiredChestStore
{
    private const string ItemColumns = "id,user_id AS OwnerId,room_id AS RoomId,base_item AS BaseItem,extra_data AS ExtraData,limited_number AS LimitedNumber,limited_stack AS LimitedStack,chest_item_id AS ChestId,chest_random_order AS RandomKey,chest_transaction_id AS DepositTransaction";

    public WiredChestSnapshot? Load(Item chest)
    {
        if (!WiredChestFurniture.IsChest(chest.Definition)) {
            return null;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var parent = connection.QuerySingleOrDefault<ItemRow>($"SELECT {ItemColumns} FROM items WHERE id=@Id FOR UPDATE", new { chest.Id }, transaction);

        if (parent == null || parent.RoomId != chest.RoomId || parent.OwnerId != chest.OwnerId) {
            return null;
        }

        var capacity = chest.Definition.ItemName.Contains("_starter", StringComparison.Ordinal) ? 100
            : chest.Definition.InteractionType == InteractionType.WiredChestCoins ? 5000 : 1000;
        connection.Execute("INSERT IGNORE INTO wired_chests (item_id,kind,settings) VALUES (@Id,@kind,@settings)",
            new { chest.Id, kind = (int)WiredChestFurniture.Kind(chest.Definition), settings = JsonSerializer.Serialize(new WiredChestSettings { Capacity = capacity }) }, transaction);
        var row = connection.QuerySingle<ChestRow>("SELECT item_id AS Id,kind,coins,capacity_level AS Level,settings FROM wired_chests WHERE item_id=@Id FOR UPDATE", new { chest.Id }, transaction);
        var stock = connection.Query<ItemRow>($"SELECT {ItemColumns} FROM items WHERE chest_item_id=@Id ORDER BY chest_transaction_id,id", new { chest.Id }, transaction).ToArray();
        var items = stock.Select(Inventory).ToArray();
        transaction.Commit();

        return Snapshot(row, parent, items) with { RandomKeys = stock.ToDictionary(item => item.Id, item => item.RandomKey), DepositTransactions = stock.ToDictionary(item => item.Id, item => item.DepositTransaction) };
    }

    public bool SaveSettings(Item chest, int userId, bool canModify, bool roomOwner, Func<WiredChestSettings, WiredChestSettings> change)
    {
        if (Load(chest) == null) {
            return false;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var parent = connection.QuerySingleOrDefault<ItemRow>($"SELECT {ItemColumns} FROM items WHERE id=@Id FOR UPDATE", new { chest.Id }, transaction);

        if (parent == null || parent.RoomId != chest.RoomId || parent.OwnerId != chest.OwnerId) {
            return false;
        }

        var row = connection.QuerySingle<ChestRow>("SELECT item_id AS Id,kind,coins,capacity_level AS Level,settings FROM wired_chests WHERE item_id=@Id FOR UPDATE", new { chest.Id }, transaction);
        var old = Settings(row);
        var proposed = change(old);
        var requested = proposed;
        // Historical Turbo fadfe2a5: coin controls send unset preview values; an unset furni amount preserves its stored value.
        proposed = proposed with
        {
            PreviewMode = row.Kind == (int)WiredChestKind.Coins ? old.PreviewMode : proposed.PreviewMode,
            PreviewAmount = row.Kind == (int)WiredChestKind.Coins || proposed.PreviewAmount < 1 ? old.PreviewAmount : proposed.PreviewAmount
        };
        var owner = parent.OwnerId == userId;

        // Others may only lock; room ownership never authorizes an unlock.
        if (!owner && (old.Locked || !proposed.Locked || requested with { Locked = old.Locked } != old
            || !(roomOwner || old.WiredEnabled && canModify))) {
            return false;
        }

        var maximum = chest.Definition.ItemName.Contains("_starter", StringComparison.Ordinal) ? 100
            : (row.Kind == (int)WiredChestKind.Coins ? 5000 : 1000) * (row.Level + 1);

        if (proposed.Capacity < 0 || proposed.Capacity > maximum || proposed.Name.Length > 30 || proposed.Description.Length > 200
            || proposed.PreviewAmount is < 1 or > 4 || proposed.StateMode is < 0 or > 3 || proposed.PreviewMode is < 0 or > 7
            || proposed.NotifyMode is < 0 or > 1 || old.WiredEnabled && !proposed.WiredEnabled
            || chest.Definition.ItemName.Contains("_starter", StringComparison.Ordinal) && proposed.WiredEnabled) {
            return false;
        }

        connection.Execute("UPDATE wired_chests SET settings=@settings WHERE item_id=@Id", new { chest.Id, settings = JsonSerializer.Serialize(proposed) }, transaction);
        transaction.Commit();

        return true;
    }

    public WiredChestContract? LoadContract(Item item)
    {
        if (!WiredChestFurniture.IsContract(item.Definition)) {
            return null;
        }

        using var connection = database.Connection();
        var json = connection.QuerySingleOrDefault<string>("SELECT contract FROM wired_contracts WHERE item_id=@Id", new { item.Id });

        return json == null ? new() { Id = item.Id, Kind = WiredChestFurniture.ContractKind(item.Definition) }
            : JsonSerializer.Deserialize<WiredChestContract>(json)! with { Id = item.Id, Kind = WiredChestFurniture.ContractKind(item.Definition) };
    }

    public bool SaveContract(Item item, WiredChestContract contract)
    {
        if (!WiredChestFurniture.IsContract(item.Definition) || contract.Kind != WiredChestFurniture.ContractKind(item.Definition)
            || !WiredChestContracts.Valid(contract, definitions)) {
            return false;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var parent = connection.QuerySingleOrDefault<ItemRow>($"SELECT {ItemColumns} FROM items WHERE id=@Id FOR UPDATE", new { item.Id }, transaction);

        if (parent == null || parent.RoomId != item.RoomId || parent.OwnerId != item.OwnerId) {
            return false;
        }

        connection.Execute("INSERT INTO wired_contracts (item_id,contract) VALUES (@Id,@contract) ON DUPLICATE KEY UPDATE contract=VALUES(contract)",
            new { item.Id, contract = JsonSerializer.Serialize(contract with { Id = item.Id }) }, transaction);
        transaction.Commit();

        return true;
    }

    public WiredChestTransferResult Transfer(WiredChestTransfer request)
    {
        if (request.WalletDepositCredits < 0 || request.ChestIds.Length is 0 or > 100 || request.PaymentIds.Length > 500
            || request.PaymentIds.Distinct().Count() != request.PaymentIds.Length || request.Multiplier is < 1 or > 500
            || request.Reward.Any(node => node.Amount < 1) || request.Order is < 0 or > 2) {
            return WiredChestTransferResult.Refused(WiredChestFailure.Invalid);
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        // The inventory owner is the same row locked by Plus inventory clearing and wallet persistence.
        var storedCredits = connection.QuerySingleOrDefault<int?>("SELECT credits FROM users WHERE id=@UserId FOR UPDATE", request, transaction);

        if (storedCredits == null) {
            return WiredChestTransferResult.Refused(WiredChestFailure.Invalid);
        }

        var previous = connection.QuerySingleOrDefault<string>("SELECT result FROM wired_chest_transactions WHERE operation_id=@operation", new { operation = request.OperationId.ToString("N") }, transaction);

        if (previous != null) {
            var replay = JsonSerializer.Deserialize<StoredResult>(previous)!;

            return new(null, replay.Credits, [], [], replay.Figures, Replayed: true);
        }

        var ids = request.ChestIds.Distinct().Order().ToArray();
        var parents = connection.Query<ItemRow>($"SELECT {ItemColumns} FROM items WHERE id IN @ids ORDER BY id FOR UPDATE", new { ids }, transaction).ToDictionary(row => row.Id);
        var rows = connection.Query<ChestRow>("SELECT item_id AS Id,kind,coins,capacity_level AS Level,settings FROM wired_chests WHERE item_id IN @ids ORDER BY item_id FOR UPDATE", new { ids }, transaction).ToArray();

        if (parents.Count != ids.Length || rows.Length != ids.Length || parents.Values.Any(row => row.RoomId != request.RoomId)) {
            return WiredChestTransferResult.Refused(WiredChestFailure.ChestNotInRoom);
        }

        var contents = connection.Query<ItemRow>($"SELECT {ItemColumns} FROM items WHERE chest_item_id IN @ids ORDER BY chest_transaction_id,id FOR UPDATE", new { ids }, transaction).ToArray();
        var chests = rows.Select(row => Snapshot(row, parents[row.Id], contents.Where(item => item.ChestId == row.Id).Select(Inventory).ToArray())).ToArray();

        if (chests.Any(chest => request.Wired ? !chest.Usable
            : (request.PaymentIds.Length != 0 || request.WalletDepositCredits > 0) && !chest.CanDeposit((uint)request.UserId, request.CanModify)
                || request.Reward.Length > 0 && !chest.CanWithdraw((uint)request.UserId, request.CanModify))) {
            return WiredChestTransferResult.Refused(WiredChestFailure.NoOrLockedChests);
        }

        var paymentRows = request.PaymentIds.Length == 0 ? [] : connection.Query<ItemRow>($"SELECT {ItemColumns} FROM items WHERE id IN @PaymentIds ORDER BY id FOR UPDATE", request, transaction).ToArray();

        if (paymentRows.Length != request.PaymentIds.Length || paymentRows.Any(row => row.OwnerId != request.UserId || row.RoomId != 0 || row.ChestId != null)
            || paymentRows.Any(row => !definitions.Items.TryGetValue(row.BaseItem, out var definition) || !definition.AllowTrade
                || WiredChestFurniture.IsChest(definition) || WiredChestFurniture.IsContract(definition))
            || request.PaymentIds.Length > 0 && connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_music_playlist WHERE disc_id IN @PaymentIds", request, transaction) != 0) {
            return WiredChestTransferResult.Refused(WiredChestFailure.Invalid);
        }

        var payment = paymentRows.Select(Inventory).ToArray();
        var vouchers = payment.Where(item => item.Definition.InteractionType == InteractionType.Exchange).ToArray();
        var walletCredits = request.LiveCredits ?? storedCredits.Value;

        if (request.WalletDepositCredits > walletCredits) {
            return WiredChestTransferResult.Refused(WiredChestFailure.InsufficientFunds);
        }

        var depositedCoins = vouchers.Sum(item => (long)item.Definition.BehaviourData) + request.WalletDepositCredits;

        if (depositedCoins > int.MaxValue || vouchers.Any(item => item.Definition.BehaviourData <= 0)) {
            return WiredChestTransferResult.Refused(WiredChestFailure.Invalid);
        }

        var deposits = new Dictionary<uint, uint>();
        var coinDeltas = rows.ToDictionary(row => row.Id, _ => 0L);
        var occupancy = chests.ToDictionary(chest => chest.Id, chest => (long)chest.Amount);

        foreach (var item in payment.Except(vouchers)) {
            var chest = chests.FirstOrDefault(chest => chest.Kind == WiredChestKind.Furni && occupancy[chest.Id] < chest.Settings.Capacity);

            if (chest == null) {
                return WiredChestTransferResult.Refused(WiredChestFailure.ChestFull);
            }

            deposits[item.Id] = chest.Id;
            occupancy[chest.Id]++;
        }

        var remainingCoins = depositedCoins;

        foreach (var chest in chests.Where(chest => chest.Kind == WiredChestKind.Coins)) {
            var put = Math.Min(remainingCoins, Math.Max(0, chest.Settings.Capacity - occupancy[chest.Id]));
            coinDeltas[chest.Id] += put;
            remainingCoins -= put;
        }

        if (remainingCoins != 0) {
            return WiredChestTransferResult.Refused(WiredChestFailure.ChestFull);
        }

        // Rewards are planned against pre-payment stock, matching Turbo's CanCover check.
        var availableCoins = chests.ToDictionary(chest => chest.Id, chest => chest.Coins);
        var availableItems = contents.ToList();
        var received = new List<InventoryItem>();
        long withdrawnCoins = 0;

        foreach (var node in request.Reward) {
            var remaining = node.Amount;

            if (node.ItemType == null) {
                foreach (var chest in chests.Where(chest => chest.Kind == WiredChestKind.Coins)) {
                    var take = Math.Min(remaining, availableCoins[chest.Id]);
                    availableCoins[chest.Id] -= take;
                    coinDeltas[chest.Id] -= take;
                    withdrawnCoins += take;
                    remaining -= take;
                }
            }
            else {
                var candidates = availableItems.Where(row => WiredChestItemType.Of(Inventory(row)) == node.ItemType).ToArray();

                if (request.Order == 2) {
                    Array.Reverse(candidates);
                }
                else if (request.Order == 0) {
                    candidates = candidates.OrderBy(row => row.RandomKey).ThenBy(row => row.Id).ToArray();
                }

                foreach (var row in candidates.Take(remaining)) {
                    var item = Inventory(row);
                    item.OwnerId = (uint)request.UserId;
                    received.Add(item);
                    availableItems.Remove(row);
                    remaining--;
                }
            }

            if (remaining != 0) {
                return WiredChestTransferResult.Refused(WiredChestFailure.FundsGone);
            }
        }

        var credits = (long)walletCredits - request.WalletDepositCredits + withdrawnCoins;

        if (credits is < 0 or > int.MaxValue || coinDeltas.Any(pair => rows.Single(row => row.Id == pair.Key).Coins + pair.Value is < 0 or > int.MaxValue)) {
            return WiredChestTransferResult.Refused(WiredChestFailure.ExceedsCapacity);
        }

        var result = new WiredChestTransferResult(null, (int)credits, received.ToArray(), request.PaymentIds,
            new(request.Multiplier, deposits.Count, (int)depositedCoins, received.Count, (int)withdrawnCoins));
        connection.Execute("INSERT INTO wired_chest_transactions (operation_id,user_id,room_id,source_id,result,created_at) VALUES (@operation,@UserId,@RoomId,@SourceId,@result,UTC_TIMESTAMP(6))",
            new { operation = request.OperationId.ToString("N"), request.UserId, request.RoomId, request.SourceId, result = JsonSerializer.Serialize(new StoredResult(result.Credits, result.Figures)) }, transaction);
        var logId = connection.ExecuteScalar<long>("SELECT LAST_INSERT_ID()", transaction: transaction);

        foreach (var (itemId, chestId) in deposits) {
            RequireOne(connection.Execute("UPDATE items SET user_id=0,chest_item_id=@chestId,chest_transaction_id=@logId,chest_random_order=@randomKey WHERE id=@itemId AND user_id=@UserId AND room_id=0 AND chest_item_id IS NULL", new { itemId, chestId, logId, randomKey = Random.Shared.NextInt64(), request.UserId }, transaction));
            Entry(connection, transaction, logId, chestId, itemId, true, 0);
        }

        foreach (var voucher in vouchers) {
            RequireOne(connection.Execute("DELETE FROM items WHERE id=@Id AND user_id=@UserId AND room_id=0 AND chest_item_id IS NULL", new { voucher.Id, request.UserId }, transaction));
        }

        foreach (var item in received) {
            var old = contents.Single(row => row.Id == item.Id);
            RequireOne(connection.Execute("UPDATE items SET user_id=@UserId,chest_item_id=NULL,chest_transaction_id=NULL,chest_random_order=NULL WHERE id=@Id AND chest_item_id=@chestId", new { item.Id, request.UserId, chestId = old.ChestId }, transaction));
            Entry(connection, transaction, logId, old.ChestId!.Value, item.Id, false, 0);
        }

        foreach (var (chestId, delta) in coinDeltas.Where(pair => pair.Value != 0)) {
            RequireOne(connection.Execute("UPDATE wired_chests SET coins=coins+@delta WHERE item_id=@chestId AND coins+@delta>=0", new { chestId, delta }, transaction));
            Entry(connection, transaction, logId, chestId, null, delta > 0, Math.Abs(delta));
        }

        connection.Execute("UPDATE users SET credits=@credits WHERE id=@UserId", new { credits, request.UserId }, transaction);
        transaction.Commit();

        return result;
    }

    public WiredChestUpgradeResult Upgrade(Item chest, int userId, int count, int? liveCredits = null, int? liveDiamonds = null)
    {
        if (Load(chest) == null) {
            return new(4);
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var credits = connection.QuerySingleOrDefault<int?>("SELECT credits FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction);
        var parent = connection.QuerySingleOrDefault<ItemRow>($"SELECT {ItemColumns} FROM items WHERE id=@Id FOR UPDATE", new { chest.Id }, transaction);

        if (parent == null || credits == null || parent.RoomId != chest.RoomId || parent.OwnerId != chest.OwnerId) {
            return new(4);
        }

        if (parent.OwnerId != userId) {
            return new(7);
        }

        if (chest.Definition.ItemName.Contains("_starter", StringComparison.Ordinal)) {
            return new(10);
        }

        var row = connection.QuerySingle<ChestRow>("SELECT item_id AS Id,kind,coins,capacity_level AS Level,settings FROM wired_chests WHERE item_id=@Id FOR UPDATE", new { chest.Id }, transaction);
        var maximum = row.Kind == (int)WiredChestKind.Coins ? 19 : 9;

        if (count <= 0 || count > maximum - row.Level) {
            return new(2);
        }

        var diamonds = UserCurrencyStore.Get(connection, userId, ActivityPointType.Diamonds, transaction, forUpdate: true);
        var creditBalance = liveCredits ?? credits.Value;
        var diamondBalance = liveDiamonds ?? diamonds;
        var cost = 10 * count;

        if (creditBalance < cost) {
            return new(5);
        }

        if (diamondBalance < cost) {
            return new(6);
        }

        connection.Execute("UPDATE users SET credits=@credits WHERE id=@userId", new { credits = creditBalance - cost, userId }, transaction);
        UserCurrencyStore.Set(connection, userId, ActivityPointType.Diamonds, diamondBalance - cost, transaction);
        // The user's selected usable capacity stays unchanged; the upgraded maximum grows.
        connection.Execute("UPDATE wired_chests SET capacity_level=capacity_level+@count WHERE item_id=@Id", new { count, chest.Id }, transaction);
        transaction.Commit();

        return new(0, creditBalance - cost, diamondBalance - cost);
    }

    private InventoryItem Inventory(ItemRow row) => new()
    {
        Id = row.Id,
        OwnerId = row.OwnerId,
        Definition = definitions.Items[row.BaseItem],
        ExtraData = FurniExtraData.Load(definitions.Items[row.BaseItem], row.ExtraData, keepLegacy: true),
        UniqueNumber = row.LimitedNumber,
        UniqueSeries = row.LimitedStack
    };
    private static WiredChestSettings Settings(ChestRow row) => JsonSerializer.Deserialize<WiredChestSettings>(row.Settings) ?? throw new InvalidDataException("Missing chest settings.");
    private static WiredChestSnapshot Snapshot(ChestRow row, ItemRow parent, InventoryItem[] items) => new(row.Id, parent.OwnerId, parent.RoomId, (WiredChestKind)row.Kind, row.Coins, row.Level, Settings(row), items);
    private static void RequireOne(int count)
    {
        if (count != 1) {
            throw new DBConcurrencyException("Chest ownership changed during transfer.");
        }
    }
    private static void Entry(IDbConnection connection, IDbTransaction transaction, long logId, uint chestId, uint? itemId, bool deposit, long coins) =>
        connection.Execute("INSERT INTO wired_chest_transaction_entries (transaction_id,chest_id,item_id,is_deposit,coins) VALUES (@logId,@chestId,@itemId,@deposit,@coins)", new { logId, chestId, itemId, deposit, coins }, transaction);
    private sealed record StoredResult(int Credits, WiredChestFigures Figures);
    private sealed class ChestRow
    {
        public uint Id { get; set; }
        public int Kind { get; set; }
        public int Coins { get; set; }
        public int Level { get; set; }
        public string Settings { get; set; } = "";
    }
    private sealed class ItemRow
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public uint RoomId { get; set; }
        public uint BaseItem { get; set; }
        public string ExtraData { get; set; } = "";
        public uint LimitedNumber { get; set; }
        public uint LimitedStack { get; set; }
        public uint? ChestId { get; set; }
        public long RandomKey { get; set; }
        public long DepositTransaction { get; set; }
    }
}
