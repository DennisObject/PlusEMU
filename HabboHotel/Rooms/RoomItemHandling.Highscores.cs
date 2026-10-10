using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms.Games;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;

namespace Plus.HabboHotel.Rooms;

internal sealed record HighscoreBatch(IReadOnlyList<HighscoreWrite> Writes);
internal enum HighscoreResolution
{
    Unknown, Prior, Candidate
}
public sealed class RoomItemTransfer
{
    internal RoomItemTransfer(RoomItemHandling owner, IReadOnlyList<RoomItemTransferEntry> entries)
    {
        Owner = owner;
        Entries = entries;
    }
    internal RoomItemHandling Owner { get; }
    internal HashSet<uint> Started { get; } = [];
    internal IReadOnlyList<RoomItemTransferEntry> Entries { get; }
}
internal sealed record RoomItemTransferEntry(Item Item, long Placement, ItemDefinition Definition, uint RoomId, uint OwnerId)
{
    internal uint ItemId { get; } = Item.Id;
    internal uint BaseItem { get; } = Definition.Id;
    internal string DefinitionName { get; } = Definition.ItemName;
}

public partial class RoomItemHandling
{
    // This gate precedes item NavSync; no room/engine/packet callback is invoked while it is held.
    private readonly object _payloadGate = new();
    private readonly Dictionary<uint, HighscoreBatch> _pendingHighscores = [];
    private readonly Dictionary<uint, RoomItemTransfer> _payloadTransfers = [];

    internal bool TryRegisterHighscores(IReadOnlyList<HighscoreWrite> writes, out HighscoreBatch? batch)
    {
        lock (_payloadGate) {
            batch = null;

            if (writes.Count == 0 || writes.Select(write => write.ItemId).Distinct().Count() != writes.Count
                || writes.Any(write => _pendingHighscores.ContainsKey(write.ItemId) || _payloadTransfers.ContainsKey(write.ItemId))) {
                return false;
            }

            return WithHighscoreLocks(writes, () =>
            {
                if (writes.Any(write => !LiveHighscore(write) || write.Item.ExtraData.Serialize() != write.LivePrior)) {
                    return false;
                }

                var registered = new HighscoreBatch(writes.ToArray());

                foreach (var write in writes) {
                    _pendingHighscores.Add(write.ItemId, registered);
                }

                // Assigned below, after the non-capturing lock scope returns.
                return true;
            }) && FindRegistered(writes, out batch);
        }
    }

    internal bool TryCaptureHighscores(IReadOnlyList<HighscoreWrite> writes, IRoomHighscoreStore store, out HighscoreBatch? batch)
    {
        lock (_payloadGate) {
            batch = null;

            if (writes.Count == 0 || writes.Select(write => write.ItemId).Distinct().Count() != writes.Count
                || writes.Any(write => PayloadBlocked(write.ItemId))) {
                return false;
            }

            batch = WithHighscoreLocks<HighscoreBatch?>(writes, () =>
            {
                if (writes.Any(write => !LiveHighscore(write) || write.Item.ExtraData.Serialize() != write.LivePrior)) {
                    return null;
                }

                HighscoreWrite[] frozen;

                try {
                    var rows = store.Read(writes);

                    if (rows.Count != writes.Count) {
                        return null;
                    }

                    var byId = rows.ToDictionary(row => row.Id);
                    var captured = new List<HighscoreWrite>();

                    foreach (var write in writes) {
                        if (!byId.TryGetValue(write.ItemId, out var row) || row.RoomId != write.RoomId
                            || row.OwnerId != write.OwnerId || row.BaseItem != write.BaseItem) {
                            return null;
                        }

                        var prior = new HighscoreDataFormat();
                        prior.Store(row.ExtraData);

                        if (prior.Serialize() != write.LivePrior) {
                            return null;
                        }

                        captured.Add(write with
                        {
                            Prior = row.ExtraData,
                            Candidate = write.LiveCandidate == write.LivePrior ? row.ExtraData : write.Candidate
                        });
                    }

                    frozen = captured.ToArray();
                }
                catch {
                    return null;
                } // No write/registration has begun: capture refusal is not an uncertain commit.

                return TryRegisterHighscores(frozen, out var registered) ? registered : null;
            });

            return batch != null;
        }
    }

    private bool FindRegistered(IReadOnlyList<HighscoreWrite> writes, out HighscoreBatch? batch)
    {
        batch = _pendingHighscores.GetValueOrDefault(writes[0].ItemId);

        return batch != null;
    }

    internal HighscoreResolution ResolveHighscores(HighscoreBatch batch, IRoomHighscoreStore store, bool commit)
    {
        if (commit) {
            try {
                // Registration already happened outside all board locks. The gate is not reacquired here.
                WithHighscoreLocks(batch.Writes, () => batch.Writes.All(write => LiveHighscore(write) && write.Item.ExtraData.Serialize() == write.LivePrior) && store.Commit(batch.Writes));
            }
            catch {
                // A committed transaction may have lost its acknowledgement. Frozen candidates are reconciled, never recomputed.
            }
        }

        List<RoomItemSnapshot>? snapshots = null;
        HighscoreResolution result;

        lock (_payloadGate) {
            result = WithHighscoreLocks(batch.Writes, () =>
            {
                if (batch.Writes.Any(write => !ReferenceEquals(_pendingHighscores.GetValueOrDefault(write.ItemId), batch)
                        || !LiveHighscore(write) || write.Item.ExtraData.Serialize() != write.LivePrior && write.Item.ExtraData.Serialize() != write.LiveCandidate)) {
                    return HighscoreResolution.Unknown;
                }

                IReadOnlyList<HighscoreRow> rows;

                try {
                    rows = store.Read(batch.Writes);
                }
                catch {
                    return HighscoreResolution.Unknown;
                }

                if (rows.Count != batch.Writes.Count) {
                    return HighscoreResolution.Unknown;
                }

                var byId = rows.ToDictionary(row => row.Id);
                var candidate = batch.Writes.All(write => byId.TryGetValue(write.ItemId, out var row)
                    && row.RoomId == write.RoomId && row.OwnerId == write.OwnerId && row.BaseItem == write.BaseItem
                    && row.ExtraData == write.Candidate);
                var prior = batch.Writes.All(write => byId.TryGetValue(write.ItemId, out var row)
                    && row.RoomId == write.RoomId && row.OwnerId == write.OwnerId && row.BaseItem == write.BaseItem
                    && row.ExtraData == write.Prior);

                if (!candidate && !prior) {
                    return HighscoreResolution.Unknown;
                }

                var data = batch.Writes.Select(write =>
                {
                    var parsed = new HighscoreDataFormat();
                    parsed.Store(candidate ? write.Candidate : write.Prior);

                    return parsed;
                }).ToArray();
                snapshots = [];

                for (var i = 0; i < batch.Writes.Count; i++) {
                    var write = batch.Writes[i];
                    var changed = write.Item.ExtraData.Serialize() != data[i].Serialize();
                    write.Item.ExtraData = data[i];

                    if (candidate && changed) {
                        snapshots.Add(RoomItemSnapshot.Capture(write.Item));
                    }

                    _pendingHighscores.Remove(write.ItemId);
                }

                return candidate ? HighscoreResolution.Candidate : HighscoreResolution.Prior;
            });
        }

        if (snapshots != null) {
            foreach (var snapshot in snapshots) {
                _room.SendPacket(new ObjectUpdateComposer(snapshot));
            }
        }

        return result;
    }

    private bool LiveHighscore(HighscoreWrite write) => ReferenceEquals(GetItem(write.ItemId), write.Item)
        && ReferenceEquals(write.Item.GetRoom(), _room) && write.Item.RoomId == write.RoomId && write.RoomId == _room.Id
        && write.Item.OwnerId == write.OwnerId && write.Item.Id == write.ItemId && write.Item.Definition.Id == write.BaseItem
        && write.Item.Definition.ItemName == write.DefinitionName
        && ReferenceEquals(write.Item.Definition, write.Definition) && write.Item.Placement == write.Placement
        && !write.Item.IsTemporary && write.Item.ExtraData is HighscoreDataFormat;
    private static T WithHighscoreLocks<T>(IReadOnlyList<HighscoreWrite> writes, Func<T> action)
    {
        var ordered = writes.OrderBy(write => write.ItemId).ToArray();
        T Enter(int index)
        {
            if (index == ordered.Length) {
                return action();
            }

            lock (ordered[index].Item.NavSync) {
                return Enter(index + 1);
            }
        }

        return Enter(0);
    }

    private bool PayloadBlocked(uint id) => _pendingHighscores.ContainsKey(id) || _payloadTransfers.ContainsKey(id);
    private string? SavePayload(Item item) => PayloadBlocked(item.Id) || item.ExtraData is HighscoreDataFormat
        && (!ReferenceEquals(GetItem(item.Id), item) || !ReferenceEquals(item.GetRoom(), _room) || item.RoomId != _room.Id)
        ? null : item.ExtraData?.Serialize();

    internal bool TryReserveTransfers(IReadOnlyList<Item> items, out RoomItemTransfer? transfer)
    {
        lock (_payloadGate) {
            transfer = null;

            if (items.Select(item => item.Id).Distinct().Count() != items.Count
                || items.Any(item => item.IsTemporary || !ReferenceEquals(GetItem(item.Id), item)
                    || !ReferenceEquals(item.GetRoom(), _room) || item.RoomId != _room.Id || PayloadBlocked(item.Id))) {
                return false;
            }

            transfer = new(this, items.Select(item => new RoomItemTransferEntry(item, item.Placement, item.Definition, _room.Id, item.OwnerId)).ToArray());

            foreach (var entry in transfer.Entries) {
                _payloadTransfers.Add(entry.ItemId, transfer);
            }

            return true;
        }
    }

    internal bool TransferIsCurrent(RoomItemTransfer transfer, Item item)
    {
        lock (_payloadGate) {
            var entry = transfer.Entries.FirstOrDefault(entry => ReferenceEquals(entry.Item, item));

            return entry != null && ReferenceEquals(_payloadTransfers.GetValueOrDefault(item.Id), transfer)
                && ReferenceEquals(GetItem(item.Id), item) && ReferenceEquals(item.GetRoom(), _room)
                && item.RoomId == entry.RoomId && item.OwnerId == entry.OwnerId && item.Placement == entry.Placement
                && item.Id == entry.ItemId && item.Definition.Id == entry.BaseItem && item.Definition.ItemName == entry.DefinitionName
                && ReferenceEquals(item.Definition, entry.Definition);
        }
    }

    internal bool BeginTransferSql(RoomItemTransfer transfer, IReadOnlyList<Item> items)
    {
        lock (_payloadGate) {
            if (!ReferenceEquals(transfer.Owner, this) || items.Any(item => transfer.Started.Contains(item.Id) || !TransferIsCurrent(transfer, item))) {
                return false;
            }

            foreach (var item in items) {
                transfer.Started.Add(item.Id);
            }

            return true;
        }
    }

    internal void CancelRolledBackTransfer(RoomItemTransfer transfer, IReadOnlyList<Item> items)
    {
        lock (_payloadGate) {
            foreach (var item in items) {
                transfer.Started.Remove(item.Id);
            }

            CancelUnstartedTransfer(transfer);
        }
    }

    private bool CompletePreparedPayload(RoomItemTransfer transfer, Item item, Plus.HabboHotel.Items.DataFormat.IFurniObjectData data)
    {
        lock (_payloadGate) {
            lock (item.NavSync) {
                if (!TransferIsCurrent(transfer, item) || !ReferenceEquals(item.ExtraData, data)) {
                    return false;
                }

                _payloadTransfers.Remove(item.Id);

                return true;
            }
        }
    }

    internal void CompleteTransfer(RoomItemTransfer transfer, Item item)
    {
        lock (_payloadGate) {
            if (ReferenceEquals(_payloadTransfers.GetValueOrDefault(item.Id), transfer) && !ReferenceEquals(GetItem(item.Id), item)) {
                // Only this exact queued reference is removed; a later same-ID placement is untouched.
                _movedItems.TryRemove(new KeyValuePair<uint, Item>(item.Id, item));
                _payloadTransfers.Remove(item.Id);
            }
        }
    }

    internal void CancelUnstartedTransfer(RoomItemTransfer transfer)
    {
        lock (_payloadGate) {
            foreach (var entry in transfer.Entries) {
                if (ReferenceEquals(_payloadTransfers.GetValueOrDefault(entry.ItemId), transfer)
                    && !transfer.Started.Contains(entry.ItemId)
                    && ReferenceEquals(GetItem(entry.ItemId), entry.Item) && entry.Item.Placement == entry.Placement) {
                    _payloadTransfers.Remove(entry.ItemId);
                }
            }
        }
    }
}
