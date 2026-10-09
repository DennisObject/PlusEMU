using System.Globalization;
using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>
/// A room's variables. All callers (effects, conditions, inspector and FX) cross this seam.
/// Durable writes complete before changes enter the queue. The directory remains the authority on ownership/placement.
/// </summary>
public sealed class WiredVariableModule(uint roomId, IWiredVariableDirectory directory, IWiredVariableStore durable,
    TimeProvider clock, IWiredBuiltinVariables? builtins = null, Func<WiredVariableReference, WiredVariableDerivation?>? derive = null)
{
    private readonly MemoryWiredVariableStore _active = new();
    private readonly Queue<WiredVariableChange> _changes = new();
    private readonly object _gate = new();
    public uint RoomId => roomId;

    // The database callback commits before any active value or change event becomes visible.
    internal void PersistDefinitionConfiguration(uint definitionId, Func<WiredVariableValue?, WiredVariableDefinitionCommit> persist)
    {
        lock (_gate) {
            var key = new WiredVariableKey(definitionId, WiredVariableTarget.Global, 0);
            var committed = persist(_active.Read(key));

            if (committed.ValueWrite is not { } write) {
                return;
            }

            if (!committed.Definition.IsDurable) {
                _active.Mutate(key, _ => write.After);
            }

            if (write.Changed) {
                _changes.Enqueue(new(committed.Definition.RoomId, key,
                write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated, write.Before, write.After, 0, 1)
                { Origin = 2 });
            }
        }
    }

    /// <summary>Capture once per FX/menu flush, share across viewers, then dispose. Never retain across room changes.</summary>
    public WiredVariableReadSnapshot CaptureReads(IEnumerable<WiredVariableReference> references, WiredVariableFrame frame)
    {
        lock (_gate) {
            if (frame.RoomId != roomId) {
                throw new ArgumentException("The frame belongs to another room.", nameof(frame));
            }

            var authority = new ReadDirectory(directory);
            var resolved = references.Distinct().ToDictionary(x => x, x => Resolve(x, false, authority));
            var holders = frame.Holders.Concat([new(WiredVariableTarget.Global, 0, 0), new(WiredVariableTarget.Context, 0, 0)]).Distinct().ToArray();
            var requests = new List<(WiredVariableReference Reference, WiredVariableHolder Holder, Resolved Resolved)>();

            foreach (var (reference, resolution) in resolved) {
                if (resolution is not null) {
                    foreach (var holder in holders) {
                        if (ValidateHolder(reference, holder, frame) && (resolution.Definition?.IsDurable != true || holder.CanPersist)) {
                            requests.Add((reference, holder, resolution));
                        }
                    }
                }
            }

            var values = new Dictionary<WiredVariableKey, WiredVariableValue>();

            foreach (var group in requests.Where(x => x.Resolved.Definition is not null).GroupBy(x => Store(x.Resolved.Definition!, frame))) {
                foreach (var value in group.Key.ReadMany(group.Select(x => Key(x.Resolved.Definition!, x.Holder)).Distinct().ToArray())) {
                    values[value.Key] = value.Value;
                }
            }

            var captured = new Dictionary<(WiredVariableReference, WiredVariableHolder), WiredVariableValue>();

            foreach (var (reference, holder, resolution) in requests) {
                var definition = resolution.Definition;
                var value = resolution.Builtin is { } builtin ? builtins?.Read(builtin, holder, frame)
                    : values.GetValueOrDefault(Key(definition!, holder))
                        ?? (definition!.Target == WiredVariableTarget.Global ? new(definition.InitialValue, null, null) : null);

                if (value is not null && resolution.Convert is { } convert) {
                    value = convert(value);
                }

                if (value is not null) {
                    captured[(reference, holder)] = value;
                }
            }

            return new(roomId, captured);
        }
    }

    public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        lock (_gate) {
            if (!ValidateHolder(reference, holder, frame)) {
                return null;
            }

            var resolved = Resolve(reference, false);

            if (resolved is null) {
                return null;
            }

            WiredVariableValue? value;

            if (resolved.Builtin is { } builtin) {
                value = builtins?.Read(builtin, holder, frame);
            }
            else {
                var definition = resolved.Definition!;

                if (definition.IsDurable && !holder.CanPersist) {
                    return null;
                }

                var stored = Store(definition, frame).Read(Key(definition, holder));
                value = stored ?? (definition.Target == WiredVariableTarget.Global ? new(definition.InitialValue, null, null) : null);
            }

            return value is not null && resolved.Convert is { } convert ? convert(value) : value;
        }
    }

    public bool Mutate(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableMutation mutation,
        long value, WiredVariableFrame frame, int origin = 0) => Change(reference, holder, mutation, _ => value, frame, origin);

    /// <summary>Arithmetic reads and writes the same locked value, including through references in another room.</summary>
    // Test seam: runs between target resolution and admission, with the attempt number.
    internal Action<int>? ResolutionHook { get; set; }

    private const int AdmissionAttempts = 3;

    public bool Change(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableMutation mutation,
        Func<long, long> transform, WiredVariableFrame frame, int origin = 0, bool notifyUnchanged = false)
    {
        // v2 gate writes are admitted to the per-gate sequencer; everything else runs the original path.
        if (builtins?.SequencesGateWrites == true) {
            return ChangeAdmitted(reference, holder, mutation, transform, frame, origin, notifyUnchanged, admittedTarget: null);
        }

        Action? completed;
        bool changed;

        lock (_gate) {
            changed = ChangeLocked(reference, holder, mutation, transform, frame, origin, notifyUnchanged, out completed);
        }

        if (changed) {
            completed?.Invoke();
        }

        return changed;
    }

    // Aliases are resolved before admission so the gate lane sees the real target, without holding the module lock
    // across it. The admitted target is then compared with the fresh authorized resolution under the lock: a fresh
    // write retries admission a few times, a replay (which carries its admitted target) refuses on any mismatch.
    private bool ChangeAdmitted(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableMutation mutation,
        Func<long, long> transform, WiredVariableFrame frame, int origin, bool notifyUnchanged, WiredVariableReference? admittedTarget)
    {
        for (var attempt = 1; attempt <= (admittedTarget is null ? AdmissionAttempts : 1); attempt++) {
            var target = admittedTarget ?? WriteTarget(reference);
            ResolutionHook?.Invoke(attempt);
            var effective = transform;
            var admission = WiredAdmission.Proceed;
            IDisposable? scope = null;

            if (builtins != null) {
                // Native gates admit bounded room state; custom variable arithmetic remains signed 64-bit.
                Func<int, int> bounded = current =>
                {
                    var next = transform(current);

                    return next is >= int.MinValue and <= int.MaxValue ? (int)next : current;
                };
                var supplied = bounded;
                scope = builtins.Admit(target, holder, ref bounded,
                    replayed => () => ChangeAdmitted(reference, holder, mutation,
                        current => current is >= int.MinValue and <= int.MaxValue ? replayed((int)current) : current,
                        frame, origin, notifyUnchanged, target),
                    () => admittedTarget is not null || WriteTarget(reference) == target, out admission);

                if (!ReferenceEquals(supplied, bounded)) {
                    var admitted = bounded;
                    effective = current => current is >= int.MinValue and <= int.MaxValue ? admitted((int)current) : current;
                }
            }

            // The admission, if any, is held until the completion callback has run.
            using (scope) {
                if (admission == WiredAdmission.Deferred) {
                    return true;
                }

                if (admission == WiredAdmission.Stale) {
                    continue;
                }

                // A transform already evaluated against an old target is never reused or re-run.
                var evaluated = !ReferenceEquals(effective, transform);
                var changed = ChangeIfTargetHolds(reference, holder, mutation, effective, frame, origin, notifyUnchanged, target,
                    retryable: admittedTarget is null && !evaluated);

                if (changed is { } result) {
                    return result;
                }
            }
        }

        return false;
    }

    // Null when the target moved and a fresh write may retry admission.
    private bool? ChangeIfTargetHolds(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableMutation mutation,
        Func<long, long> transform, WiredVariableFrame frame, int origin, bool notifyUnchanged, WiredVariableReference target, bool retryable)
    {
        Action? completed;
        bool changed;

        lock (_gate) {
            if (WriteTargetLocked(reference) != target) {
                return retryable ? null : false;
            }

            changed = ChangeLocked(reference, holder, mutation, transform, frame, origin, notifyUnchanged, out completed);
        }

        if (changed) {
            completed?.Invoke();
        }

        return changed;
    }

    private bool ChangeLocked(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableMutation mutation,
        Func<long, long> transform, WiredVariableFrame frame, int origin, bool notifyUnchanged, out Action? completed)
    {
        completed = null;

        if (frame.Depth >= 32 || !ValidateHolder(reference, holder, frame)) {
            return false;
        }

        var resolved = Resolve(reference, true);

        if (resolved is null) {
            return false;
        }

        if (resolved.Builtin is { } builtin) {
            if (mutation != WiredVariableMutation.Set) {
                // Presence flags are not numeric operands; their dedicated source owns creation/removal.
                // The supported area flags cannot be intercepted according to the native variable metadata.
                return builtins?.MutatePresence(builtin, holder, mutation, frame) == true;
            }

            var current = builtins?.Read(builtin, holder, frame);

            if (current is null) {
                return false;
            }

            var next = transform(current.Value);

            if (next is < int.MinValue or > int.MaxValue || next == current.Value || !builtins!.Write(builtin, holder, (int)next, frame, out completed)) {
                return false;
            }

            var after = builtins.Read(builtin, holder, frame);

            if (after is not null && after.Value != current.Value) {
                _changes.Enqueue(new(roomId, new(0, holder.Target, holder.StorageId), WiredVariableChangeKind.Updated,
                    current, after, holder.EntityId, frame.Depth + 1)
                { Origin = origin, InternalKey = RoomWiredBuiltinVariables.Normalize(builtin.Token) });
            }

            return true;
        }

        var definition = resolved.Definition!;

        if (definition.IsDurable && (!holder.CanPersist || holder.Target == WiredVariableTarget.User && holder.StableId <= 0)) {
            return false;
        }

        if (mutation == WiredVariableMutation.Set && !definition.HasValue) {
            return false;
        }

        if (definition.Target == WiredVariableTarget.Global && mutation != WiredVariableMutation.Set) {
            return false;
        }

        var key = Key(definition, holder);
        var store = Store(definition, frame);

        if (definition.Target == WiredVariableTarget.Context) {
            if (mutation == WiredVariableMutation.Give && frame.Context.Read(key) != null) {
                return false;
            }

            if (mutation is WiredVariableMutation.Set or WiredVariableMutation.Remove) {
                store = frame.Context.Owner(key) ?? frame.Context;
            }
        }

        var acceptedUnchangedWrite = false;
        var write = store.Mutate(key, previous =>
        {
            var current = previous ?? (definition.Target == WiredVariableTarget.Global ? new(definition.InitialValue, null, null) : null);

            if (mutation == WiredVariableMutation.Give && current is not null) {
                return previous;
            }

            if (mutation is WiredVariableMutation.Set or WiredVariableMutation.Remove && current is null) {
                return previous;
            }

            if (mutation == WiredVariableMutation.Remove) {
                return null;
            }

            var next = definition.HasValue ? transform(current?.Value ?? 0) : 1;

            if (mutation == WiredVariableMutation.Set && !notifyUnchanged && previous is not null && previous.Value == next) {
                return previous;
            }

            acceptedUnchangedWrite = notifyUnchanged && mutation == WiredVariableMutation.Set;
            var now = clock.GetUtcNow();

            return new(next, previous is null || mutation == WiredVariableMutation.Replace ? now : previous.CreatedAt, now);
        }, definition.IsDurable ? resolved.Authorization : null);

        if (!write.Changed && !acceptedUnchangedWrite) {
            return false;
        }

        _changes.Enqueue(new(definition.RoomId, key, write.After is null ? WiredVariableChangeKind.Removed :
            write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated,
            write.Before, write.After, holder.EntityId, frame.Depth + 1)
        { Origin = origin });

        return true;
    }

    /// <summary>Publish a complete speech match into one firing only after every destination is authorized.</summary>
    public bool CaptureContextValues(IReadOnlyDictionary<uint, int> values, WiredVariableFrame frame) =>
        CaptureContextValues(values.ToDictionary(pair => pair.Key, pair => (long)pair.Value), frame);

    public bool CaptureContextValues(IReadOnlyDictionary<uint, long> values, WiredVariableFrame frame)
    {
        lock (_gate) {
            if (frame.RoomId != roomId || frame.Depth >= 32 || values.Count > 8) {
                return false;
            }

            var destinations = new List<(WiredVariableDefinition Definition, long Value)>();

            foreach (var (id, value) in values) {
                var resolved = Resolve(new(WiredVariableTarget.Context, $"custom:{id}"), true);

                if (resolved?.Definition is not { Target: WiredVariableTarget.Context, HasValue: true } definition) {
                    return false;
                }

                destinations.Add((definition, value));
            }

            DateTimeOffset? capturedAt = null;
            DateTimeOffset CaptureNow() => capturedAt ??= clock.GetUtcNow();

            foreach (var (definition, value) in destinations) {
                var key = new WiredVariableKey(definition.ItemId, WiredVariableTarget.Context, 0);
                var write = frame.Context.Mutate(key, before => before?.Value == value ? before
                    : new(value, before is null ? CaptureNow() : before.CreatedAt, CaptureNow()));

                if (write.Changed) {
                    _changes.Enqueue(new(roomId, key, write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated,
                    write.Before, write.After, 0, frame.Depth + 1));
                }
            }

            return true;
        }
    }

    /// <summary>Seed a room variable once after loading its saved definition; never overwrite a durable current value.</summary>
    public bool InitializeGlobal(uint definitionId)
    {
        lock (_gate) {
            var resolved = Resolve(new(WiredVariableTarget.Global, $"custom:{definitionId}"), true);

            if (resolved?.Definition is not { Target: WiredVariableTarget.Global, Link: null } definition || definition.ItemId != definitionId) {
                return false;
            }

            var frame = new WiredVariableFrame(roomId, []);
            DateTimeOffset? capturedAt = null;
            var result = Store(definition, frame).Mutate(new(definitionId, WiredVariableTarget.Global, 0),
                before => before ?? new(definition.InitialValue, capturedAt ??= clock.GetUtcNow(), capturedAt.Value),
                definition.IsDurable ? resolved.Authorization : null);

            return result.After is not null;
        }
    }

    /// <summary>Editor assignment reports acceptance, including an unchanged value; rejected authorization returns false.</summary>
    public bool SaveGlobalValue(uint definitionId, long value)
    {
        lock (_gate) {
            var resolved = Resolve(new(WiredVariableTarget.Global, $"custom:{definitionId}"), true);

            if (resolved?.Definition is not { Target: WiredVariableTarget.Global, Link: null } definition || definition.ItemId != definitionId) {
                return false;
            }

            var key = new WiredVariableKey(definitionId, WiredVariableTarget.Global, 0);
            DateTimeOffset? capturedAt = null;
            var write = Store(definition, new(roomId, [])).Mutate(key, before => before?.Value == value ? before
                : new(value, before is null ? capturedAt ??= clock.GetUtcNow() : before.CreatedAt,
                    capturedAt ??= clock.GetUtcNow()),
                definition.IsDurable ? resolved.Authorization : null);

            if (write.After is null) {
                return false;
            }

            if (write.Changed) {
                _changes.Enqueue(new(definition.RoomId, key, write.Before is null ? WiredVariableChangeKind.Created : WiredVariableChangeKind.Updated,
                write.Before, write.After, 0, 1)
                { Origin = 2 });
            }

            return true;
        }
    }

    public IReadOnlyList<WiredVariableChange> DrainChanges()
    {
        lock (_gate) {
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
        lock (_gate) {
            var removed = durable.DeleteDefinition(definitionId);

            return removed + _active.DeleteDefinition(definitionId);
        }
    }

    /// <summary>Owner-only menu operation. Clears values, retaining the definition and allowing future assignments.</summary>
    public int ClearValues(uint definitionId, WiredVariableTarget target, WiredVariableFrame frame)
    {
        lock (_gate) {
            if (frame.RoomId != roomId || frame.Depth >= 32 || target is not (WiredVariableTarget.User or WiredVariableTarget.Furni)) {
                return 0;
            }

            var resolved = Resolve(new(target, $"custom:{definitionId}"), true);

            if (resolved?.Definition is not { } definition || resolved.Authorization is not { } authorization) {
                return 0;
            }

            var removed = Store(definition, frame).ClearValues(definition.ItemId, authorization);

            foreach (var (key, value) in removed) {
                var entityId = frame.Holders.FirstOrDefault(x => x.Target == key.Target && x.StorageId == key.HolderId).EntityId;
                _changes.Enqueue(new(definition.RoomId, key, WiredVariableChangeKind.Removed, value, null, entityId, frame.Depth + 1) { Origin = 2 });
            }

            return removed.Count;
        }
    }

    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetStoredHolders(uint definitionId)
    {
        lock (_gate) {
            var local = directory.Find(definitionId);
            var resolved = local?.RoomId == roomId ? Resolve(new(local.Target, local.Token), false) : null;

            if (resolved?.Definition is not { } definition || definition.Target == WiredVariableTarget.Context) {
                return new Dictionary<WiredVariableKey, WiredVariableValue>();
            }

            var values = definition.IsDurable ? durable.GetHolders(definition.ItemId) : _active.GetHolders(definition.ItemId);

            return values.ToDictionary(x => x.Key with { DefinitionId = definitionId }, x => x.Value);
        }
    }

    public IReadOnlyList<WiredVariableDescription> DescribeDefinitions(IEnumerable<uint> definitionIds)
    {
        lock (_gate) {
            var authority = new ReadDirectory(directory);
            var result = new List<WiredVariableDescription>();

            foreach (var id in definitionIds.Distinct().Take(4096)) {
                var local = authority.Find(id);

                if (local?.RoomId != roomId) {
                    continue;
                }

                var resolved = Resolve(new(local.Target, local.Token), false, authority);

                if (resolved is null) {
                    continue;
                }

                var readOnly = local.Target == WiredVariableTarget.Context || resolved.Authorization?.Lineage.Any(x => x.Link?.ReadOnly == true) == true;
                result.Add(new(local, HasValue(resolved), readOnly) { IsBuiltin = resolved.Builtin is not null });
            }

            return result.OrderBy(x => x.CatalogTarget).ThenBy(x => x.Definition.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Definition.ItemId).ToArray();
        }
    }

    public WiredVariableHolderPage ReadHolderPage(uint definitionId, int page, int size, int sort,
        IReadOnlyCollection<long>? holderFilter = null, IReadOnlyDictionary<long, string>? names = null)
    {
        lock (_gate) {
            var local = directory.Find(definitionId);
            var resolved = local?.RoomId == roomId ? Resolve(new(local.Target, local.Token), false) : null;

            if (resolved?.Definition is not { } definition || definition.Target == WiredVariableTarget.Context) {
                return new(0, Math.Max(1, page), Math.Clamp(size, 1, 200), []);
            }

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

    private bool HasValue(Resolved resolved) => resolved.Definition?.HasValue
        ?? (resolved.Builtin is { } builtin && builtins?.HasValue(builtin) == true);

    internal WiredVariableReference WriteTarget(WiredVariableReference reference)
    {
        lock (_gate) {
            return WriteTargetLocked(reference);
        }
    }

    // The authorized final target: the builtin it echoes, otherwise the stored definition at the end of the alias chain.
    private WiredVariableReference WriteTargetLocked(WiredVariableReference reference)
        => Resolve(reference, true) is { } resolved
            ? resolved.Builtin ?? new(reference.Target, resolved.Definition!.Token)
            : reference;

    private Resolved? Resolve(WiredVariableReference reference, bool writing, IWiredVariableDirectory? readDirectory = null)
    {
        var authority = readDirectory ?? directory;

        if (derive?.Invoke(reference) is { } derived) {
            if (writing || derived.Source == reference) {
                return null;
            }

            var source = Resolve(derived.Source, false, authority);

            return source is null || derived.RequiresValue && !HasValue(source)
                || derived.RequiresTimestamps && source.Definition is null ? null : source with { Convert = derived.Convert };
        }

        var visited = new HashSet<uint>();
        var lineage = ImmutableArray.CreateBuilder<WiredVariableDefinition>();
        var expectedRoom = roomId;
        var owner = authority.GetRoomOwner(roomId);

        if (owner is null or 0) {
            return null;
        }

        while (true) {
            if (reference.Token.StartsWith("internal:", StringComparison.Ordinal)) {
                return expectedRoom == roomId ? new(null, reference, new(roomId, owner.Value, lineage.ToImmutable())) : null;
            }

            if (!TryDefinitionId(reference.Token, out var id) || !visited.Add(id) || visited.Count > 32) {
                return null;
            }

            var definition = authority.Find(id);

            if (definition is null || definition.RoomId != expectedRoom || definition.Target != reference.Target
                || definition.OwnerId != owner || authority.GetRoomOwner(expectedRoom) != owner) {
                return null;
            }

            lineage.Add(definition);

            if (definition.Link is not { } link) {
                return new(definition, null, new(roomId, owner.Value, lineage.ToImmutable()));
            }

            if (writing && link.ReadOnly) {
                return null;
            }

            if (link.Source.Target != reference.Target) {
                return null;
            }

            if (link.SourceRoomId != definition.RoomId) {
                if (reference.Target is not (WiredVariableTarget.User or WiredVariableTarget.Global)
                    || !TryDefinitionId(link.Source.Token, out var sourceId)) {
                    return null;
                }

                var source = authority.Find(sourceId);

                if (source is null || source.Availability != WiredVariableAvailability.Shared || source.Link is not null) {
                    return null;
                }
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
            if (!_definitions.TryGetValue(itemId, out var value)) {
                _definitions[itemId] = value = source.Find(itemId);
            }

            return value;
        }
        public uint? GetRoomOwner(uint roomId)
        {
            if (!_owners.TryGetValue(roomId, out var value)) {
                _owners[roomId] = value = source.GetRoomOwner(roomId);
            }

            return value;
        }
    }
}
