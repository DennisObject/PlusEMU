using System.Globalization;
using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>
/// A room's variables. All callers (effects, conditions, inspector and FX) cross this seam.
/// Durable writes complete before changes enter the queue. The directory remains the authority on ownership/placement.
/// </summary>
public sealed class WiredVariableModule(uint roomId, IWiredVariableDirectory directory, IWiredVariableStore durable,
    Func<long> nowMs, IWiredBuiltinVariables? builtins = null, Func<WiredVariableReference, WiredVariableDerivation?>? derive = null)
{
    private readonly MemoryWiredVariableStore _active = new();
    private readonly Queue<WiredVariableChange> _changes = new();
    private readonly object _gate = new();
    public uint RoomId => roomId;

    // The database callback commits before any active value or change event becomes visible.
    internal void PersistDefinitionConfiguration(uint definitionId, Func<WiredVariableValue?, WiredVariableDefinitionCommit> persist)
    {
        lock (_gate)
        {
            var key = new WiredVariableKey(definitionId, WiredVariableTarget.Global, 0);
            var committed = persist(_active.Read(key));
            if (committed.ValueWrite is not { } write) return;
            if (!committed.Definition.IsDurable) _active.Mutate(key, _ => write.After);
            if (write.Changed) _changes.Enqueue(new(committed.Definition.RoomId, key,
                write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated, write.Before, write.After, 0, 1));
        }
    }

    /// <summary>Capture once per FX/menu flush, share across viewers, then dispose. Never retain across room changes.</summary>
    public WiredVariableReadSnapshot CaptureReads(IEnumerable<WiredVariableReference> references, WiredVariableFrame frame)
    {
        lock (_gate)
        {
            if (frame.RoomId != roomId) throw new ArgumentException("The frame belongs to another room.", nameof(frame));
            var authority = new ReadDirectory(directory);
            var resolved = references.Distinct().ToDictionary(x => x, x => Resolve(x, false, authority));
            var holders = frame.Holders.Concat([new(WiredVariableTarget.Global, 0, 0), new(WiredVariableTarget.Context, 0, 0)]).Distinct().ToArray();
            var requests = new List<(WiredVariableReference Reference, WiredVariableHolder Holder, Resolved Resolved)>();
            foreach (var (reference, resolution) in resolved)
                if (resolution is not null)
                    foreach (var holder in holders)
                        if (ValidateHolder(reference, holder, frame) && (resolution.Definition?.IsDurable != true || holder.CanPersist))
                            requests.Add((reference, holder, resolution));
            var values = new Dictionary<WiredVariableKey, WiredVariableValue>();
            foreach (var group in requests.Where(x => x.Resolved.Definition is not null).GroupBy(x => Store(x.Resolved.Definition!, frame)))
                foreach (var value in group.Key.ReadMany(group.Select(x => Key(x.Resolved.Definition!, x.Holder)).Distinct().ToArray()))
                    values[value.Key] = value.Value;
            var captured = new Dictionary<(WiredVariableReference, WiredVariableHolder), WiredVariableValue>();
            foreach (var (reference, holder, resolution) in requests)
            {
                var definition = resolution.Definition;
                var value = resolution.Builtin is { } builtin ? builtins?.Read(builtin, holder, frame)
                    : values.GetValueOrDefault(Key(definition!, holder))
                        ?? (definition!.Target == WiredVariableTarget.Global ? new(definition.InitialValue, 0, 0) : null);
                if (value is not null && resolution.Convert is { } convert) value = convert(value);
                if (value is not null) captured[(reference, holder)] = value;
            }
            return new(roomId, captured);
        }
    }

    public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        lock (_gate)
        {
            if (!ValidateHolder(reference, holder, frame)) return null;
            var resolved = Resolve(reference, false);
            if (resolved is null) return null;
            if (resolved.Builtin is { } builtin) return builtins?.Read(builtin, holder, frame);
            var definition = resolved.Definition!;
            if (definition.IsDurable && !holder.CanPersist) return null;
            var stored = Store(definition, frame).Read(Key(definition, holder));
            var value = stored ?? (definition.Target == WiredVariableTarget.Global ? new(definition.InitialValue, 0, 0) : null);
            return value is not null && resolved.Convert is { } convert ? convert(value) : value;
        }
    }

    public bool Mutate(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableMutation mutation,
        int value, WiredVariableFrame frame) => Change(reference, holder, mutation, _ => value, frame);

    /// <summary>Arithmetic reads and writes the same locked value, including through references in another room.</summary>
    public bool Change(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableMutation mutation,
        Func<int, int> transform, WiredVariableFrame frame)
    {
        lock (_gate)
        {
            if (frame.Depth >= 32 || !ValidateHolder(reference, holder, frame)) return false;
            var resolved = Resolve(reference, true);
            if (resolved is null) return false;
            if (resolved.Builtin is { } builtin)
            {
                if (mutation != WiredVariableMutation.Set) return false;
                var current = builtins?.Read(builtin, holder, frame);
                return current is not null && builtins!.Write(builtin, holder, transform(current.Value), frame);
            }
            var definition = resolved.Definition!;
            if (definition.IsDurable && (!holder.CanPersist || holder.Target == WiredVariableTarget.User && holder.StableId <= 0)) return false;
            if (mutation == WiredVariableMutation.Set && !definition.HasValue) return false;
            if (definition.Target == WiredVariableTarget.Global && mutation != WiredVariableMutation.Set) return false;
            var key = Key(definition, holder);
            var write = Store(definition, frame).Mutate(key, previous =>
            {
                var current = previous ?? (definition.Target == WiredVariableTarget.Global ? new(definition.InitialValue, 0, 0) : null);
                if (mutation == WiredVariableMutation.Give && current is not null) return previous;
                if (mutation is WiredVariableMutation.Set or WiredVariableMutation.Remove && current is null) return previous;
                if (mutation == WiredVariableMutation.Remove) return null;
                var next = definition.HasValue ? transform(current?.Value ?? 0) : 1;
                if (mutation == WiredVariableMutation.Set && previous is not null && previous.Value == next) return previous;
                var now = nowMs();
                return new(next, previous?.CreatedAtMs ?? now, now);
            }, definition.IsDurable ? resolved.Authorization : null);
            if (!write.Changed) return false;
            _changes.Enqueue(new(definition.RoomId, key, write.After is null ? WiredVariableChangeKind.Removed :
                write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated,
                write.Before, write.After, holder.EntityId, frame.Depth + 1));
            return true;
        }
    }

    /// <summary>Publish a complete speech match into one firing only after every destination is authorized.</summary>
    public bool CaptureContextValues(IReadOnlyDictionary<uint, int> values, WiredVariableFrame frame)
    {
        lock (_gate)
        {
            if (frame.RoomId != roomId || frame.Depth >= 32 || values.Count > 8) return false;
            var destinations = new List<(WiredVariableDefinition Definition, int Value)>();
            foreach (var (id, value) in values)
            {
                var resolved = Resolve(new(WiredVariableTarget.Context, $"custom:{id}"), true);
                if (resolved?.Definition is not { Target: WiredVariableTarget.Context, HasValue: true } definition) return false;
                destinations.Add((definition, value));
            }
            foreach (var (definition, value) in destinations)
            {
                var key = new WiredVariableKey(definition.ItemId, WiredVariableTarget.Context, 0);
                var write = frame.Context.Mutate(key, before => before?.Value == value ? before : new(value, before?.CreatedAtMs ?? nowMs(), nowMs()));
                if (write.Changed) _changes.Enqueue(new(roomId, key, write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated,
                    write.Before, write.After, 0, frame.Depth + 1));
            }
            return true;
        }
    }

    /// <summary>Seed a room variable once after loading its saved definition; never overwrite a durable current value.</summary>
    public bool InitializeGlobal(uint definitionId)
    {
        lock (_gate)
        {
            var resolved = Resolve(new(WiredVariableTarget.Global, $"custom:{definitionId}"), true);
            if (resolved?.Definition is not { Target: WiredVariableTarget.Global, Link: null } definition || definition.ItemId != definitionId) return false;
            var frame = new WiredVariableFrame(roomId, []);
            var result = Store(definition, frame).Mutate(new(definitionId, WiredVariableTarget.Global, 0),
                before => before ?? new(definition.InitialValue, nowMs(), nowMs()), definition.IsDurable ? resolved.Authorization : null);
            return result.After is not null;
        }
    }

    /// <summary>Editor assignment reports acceptance, including an unchanged value; rejected authorization returns false.</summary>
    public bool SaveGlobalValue(uint definitionId, int value)
    {
        lock (_gate)
        {
            var resolved = Resolve(new(WiredVariableTarget.Global, $"custom:{definitionId}"), true);
            if (resolved?.Definition is not { Target: WiredVariableTarget.Global, Link: null } definition || definition.ItemId != definitionId) return false;
            var key = new WiredVariableKey(definitionId, WiredVariableTarget.Global, 0);
            var write = Store(definition, new(roomId, [])).Mutate(key, before => before?.Value == value ? before
                : new(value, before?.CreatedAtMs ?? nowMs(), nowMs()), definition.IsDurable ? resolved.Authorization : null);
            if (write.After is null) return false;
            if (write.Changed) _changes.Enqueue(new(definition.RoomId, key, write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated,
                write.Before, write.After, 0, 1));
            return true;
        }
    }

    public IReadOnlyList<WiredVariableChange> DrainChanges()
    {
        lock (_gate)
        {
            var result = _changes.ToArray();
            _changes.Clear();
            return result;
        }
    }

    // Departure only clears active values. Pickup/room unload must never call DeleteDefinition.
    public void HolderLeft(WiredVariableHolder holder) => _active.RemoveHolder(holder.Target, holder.StorageId);
    public void DetachDefinition(uint definitionId) => _active.DeleteDefinition(definitionId);

    /// <summary>Call only after the owning definition item is actually deleted; deletion failure propagates.</summary>
    public int DeleteDefinition(uint definitionId)
    {
        lock (_gate)
        {
            var removed = durable.DeleteDefinition(definitionId);
            return removed + _active.DeleteDefinition(definitionId);
        }
    }

    /// <summary>Owner-only menu operation. Clears values, retaining the definition and allowing future assignments.</summary>
    public int ClearValues(uint definitionId, WiredVariableTarget target, WiredVariableFrame frame)
    {
        lock (_gate)
        {
            if (frame.RoomId != roomId || frame.Depth >= 32 || target is not (WiredVariableTarget.User or WiredVariableTarget.Furni)) return 0;
            var resolved = Resolve(new(target, $"custom:{definitionId}"), true);
            if (resolved?.Definition is not { } definition || resolved.Authorization is not { } authorization) return 0;
            var removed = Store(definition, frame).ClearValues(definition.ItemId, authorization);
            foreach (var (key, value) in removed)
            {
                var entityId = frame.Holders.FirstOrDefault(x => x.Target == key.Target && x.StorageId == key.HolderId).EntityId;
                _changes.Enqueue(new(definition.RoomId, key, WiredVariableChangeKind.Removed, value, null, entityId, frame.Depth + 1));
            }
            return removed.Count;
        }
    }

    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetStoredHolders(uint definitionId)
    {
        lock (_gate)
        {
            var local = directory.Find(definitionId);
            var resolved = local?.RoomId == roomId ? Resolve(new(local.Target, local.Token), false) : null;
            if (resolved?.Definition is not { } definition || definition.Target == WiredVariableTarget.Context) return new Dictionary<WiredVariableKey, WiredVariableValue>();
            var values = definition.IsDurable ? durable.GetHolders(definition.ItemId) : _active.GetHolders(definition.ItemId);
            return values.ToDictionary(x => x.Key with { DefinitionId = definitionId }, x => x.Value);
        }
    }

    public IReadOnlyList<WiredVariableDescription> DescribeDefinitions(IEnumerable<uint> definitionIds)
    {
        lock (_gate)
        {
            var authority = new ReadDirectory(directory); var result = new List<WiredVariableDescription>();
            foreach (var id in definitionIds.Distinct().Take(4096))
            {
                var local = authority.Find(id);
                if (local?.RoomId != roomId) continue;
                var resolved = Resolve(new(local.Target, local.Token), false, authority);
                if (resolved is null) continue;
                var readOnly = local.Target == WiredVariableTarget.Context || resolved.Authorization?.Lineage.Any(x => x.Link?.ReadOnly == true) == true;
                result.Add(new(local, resolved.Definition?.HasValue ?? local.HasValue, readOnly) { IsBuiltin = resolved.Builtin is not null });
            }
            return result.OrderBy(x => x.CatalogTarget).ThenBy(x => x.Definition.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Definition.ItemId).ToArray();
        }
    }

    public WiredVariableHolderPage ReadHolderPage(uint definitionId, int page, int size, int sort,
        IReadOnlyCollection<long>? holderFilter = null, IReadOnlyDictionary<long, string>? names = null)
    {
        lock (_gate)
        {
            var local = directory.Find(definitionId);
            var resolved = local?.RoomId == roomId ? Resolve(new(local.Target, local.Token), false) : null;
            if (resolved?.Definition is not { } definition || definition.Target == WiredVariableTarget.Context)
                return new(0, Math.Max(1, page), Math.Clamp(size, 1, 200), []);
            var store = definition.IsDurable ? durable : (IWiredVariableStore)_active;
            var result = store.ReadPage(definition.ItemId, definition.Target, page, size, sort, holderFilter, names);
            return result with { Holders = result.Holders.Select(x => x with { Key = x.Key with { DefinitionId = definitionId } }).ToArray() };
        }
    }

    private bool ValidateHolder(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame) =>
        frame.RoomId == roomId && reference.Target == holder.Target && frame.Contains(holder)
        && (holder.Target is WiredVariableTarget.Context or WiredVariableTarget.Global || holder.StableId != 0 || holder.IsTemporaryFurni);

    private static WiredVariableKey Key(WiredVariableDefinition definition, WiredVariableHolder holder) =>
        new(definition.ItemId, definition.Target, holder.StorageId);
    private IWiredVariableStore Store(WiredVariableDefinition definition, WiredVariableFrame frame) =>
        definition.Target == WiredVariableTarget.Context ? frame.Context : definition.IsDurable ? durable : _active;

    private Resolved? Resolve(WiredVariableReference reference, bool writing, IWiredVariableDirectory? readDirectory = null)
    {
        var authority = readDirectory ?? directory;
        if (derive?.Invoke(reference) is { } derived)
        {
            if (writing || derived.Source == reference) return null;
            var source = Resolve(derived.Source, false, authority);
            return source is null || derived.RequiresValue && source.Definition?.HasValue != true ? null : source with { Convert = derived.Convert };
        }
        var visited = new HashSet<uint>();
        var lineage = ImmutableArray.CreateBuilder<WiredVariableDefinition>();
        var expectedRoom = roomId;
        var owner = authority.GetRoomOwner(roomId);
        if (owner is null or 0) return null;
        while (true)
        {
            if (reference.Token.StartsWith("internal:", StringComparison.Ordinal))
                return expectedRoom == roomId ? new(null, reference, new(roomId, owner.Value, lineage.ToImmutable())) : null;
            if (!TryDefinitionId(reference.Token, out var id) || !visited.Add(id) || visited.Count > 32) return null;
            var definition = authority.Find(id);
            if (definition is null || definition.RoomId != expectedRoom || definition.Target != reference.Target
                || definition.OwnerId != owner || authority.GetRoomOwner(expectedRoom) != owner) return null;
            lineage.Add(definition);
            if (definition.Link is not { } link) return new(definition, null, new(roomId, owner.Value, lineage.ToImmutable()));
            if (writing && link.ReadOnly) return null;
            if (link.Source.Target != reference.Target) return null;
            if (link.SourceRoomId != definition.RoomId)
            {
                if (reference.Target is not (WiredVariableTarget.User or WiredVariableTarget.Global)
                    || !TryDefinitionId(link.Source.Token, out var sourceId)) return null;
                var source = authority.Find(sourceId);
                if (source is null || source.Availability != WiredVariableAvailability.Shared || source.Link is not null) return null;
            }
            expectedRoom = link.SourceRoomId;
            reference = link.Source;
        }
    }

    public static bool TryDefinitionId(string token, out uint id) =>
        uint.TryParse(token.StartsWith("custom:", StringComparison.Ordinal) ? token[7..] : token,
            NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private sealed record Resolved(WiredVariableDefinition? Definition, WiredVariableReference? Builtin, WiredVariableAuthorization? Authorization, Func<WiredVariableValue, WiredVariableValue?>? Convert = null);

    private sealed class ReadDirectory(IWiredVariableDirectory source) : IWiredVariableDirectory
    {
        private readonly Dictionary<uint, WiredVariableDefinition?> _definitions = [];
        private readonly Dictionary<uint, uint?> _owners = [];
        public WiredVariableDefinition? Find(uint itemId)
        {
            if (!_definitions.TryGetValue(itemId, out var value)) _definitions[itemId] = value = source.Find(itemId);
            return value;
        }
        public uint? GetRoomOwner(uint roomId)
        {
            if (!_owners.TryGetValue(roomId, out var value)) _owners[roomId] = value = source.GetRoomOwner(roomId);
            return value;
        }
    }
}
