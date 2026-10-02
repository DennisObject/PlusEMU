using System.Globalization;
using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>
/// A room's variables. All callers (effects, conditions, inspector and FX) cross this seam.
/// Durable writes complete before changes enter the queue. The directory remains the authority on ownership/placement.
/// </summary>
public sealed class WiredVariableModule(uint roomId, IWiredVariableDirectory directory, IWiredVariableStore durable,
    Func<long> nowMs, IWiredBuiltinVariables? builtins = null)
{
    private readonly MemoryWiredVariableStore _active = new();
    private readonly Queue<WiredVariableChange> _changes = new();
    private readonly object _gate = new();
    public uint RoomId => roomId;

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
            return stored ?? (definition.Target == WiredVariableTarget.Global ? new(definition.InitialValue, 0, 0) : null);
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
    public void HolderLeft(WiredVariableHolder holder) => _active.RemoveHolder(holder.Target, holder.StableId);
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

    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetStoredHolders(uint definitionId)
    {
        var definition = directory.Find(definitionId);
        if (definition is null || definition.RoomId != roomId) return new Dictionary<WiredVariableKey, WiredVariableValue>();
        return definition.IsDurable ? durable.GetHolders(definitionId) : _active.GetHolders(definitionId);
    }

    private bool ValidateHolder(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame) =>
        frame.RoomId == roomId && reference.Target == holder.Target && frame.Contains(holder)
        && (holder.Target is WiredVariableTarget.Context or WiredVariableTarget.Global || holder.StableId != 0);

    private static WiredVariableKey Key(WiredVariableDefinition definition, WiredVariableHolder holder) =>
        new(definition.ItemId, definition.Target, holder.StableId);
    private IWiredVariableStore Store(WiredVariableDefinition definition, WiredVariableFrame frame) =>
        definition.Target == WiredVariableTarget.Context ? frame.Context : definition.IsDurable ? durable : _active;

    private Resolved? Resolve(WiredVariableReference reference, bool writing)
    {
        var visited = new HashSet<uint>();
        var lineage = ImmutableArray.CreateBuilder<WiredVariableDefinition>();
        var expectedRoom = roomId;
        var owner = directory.GetRoomOwner(roomId);
        if (owner is null or 0) return null;
        while (true)
        {
            if (reference.Token.StartsWith("internal:", StringComparison.Ordinal))
                return expectedRoom == roomId ? new(null, reference, null) : null;
            if (!TryDefinitionId(reference.Token, out var id) || !visited.Add(id) || visited.Count > 32) return null;
            var definition = directory.Find(id);
            if (definition is null || definition.RoomId != expectedRoom || definition.Target != reference.Target
                || definition.OwnerId != owner || directory.GetRoomOwner(expectedRoom) != owner) return null;
            lineage.Add(definition);
            if (definition.Link is not { } link) return new(definition, null, new(roomId, owner.Value, lineage.ToImmutable()));
            if (writing && link.ReadOnly) return null;
            if (link.Source.Target != reference.Target) return null;
            if (link.SourceRoomId != definition.RoomId)
            {
                if (reference.Target is not (WiredVariableTarget.User or WiredVariableTarget.Global)
                    || !TryDefinitionId(link.Source.Token, out var sourceId)) return null;
                var source = directory.Find(sourceId);
                if (source is null || source.Availability != WiredVariableAvailability.Shared || source.Link is not null) return null;
            }
            expectedRoom = link.SourceRoomId;
            reference = link.Source;
        }
    }

    public static bool TryDefinitionId(string token, out uint id) =>
        uint.TryParse(token.StartsWith("custom:", StringComparison.Ordinal) ? token[7..] : token,
            NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private sealed record Resolved(WiredVariableDefinition? Definition, WiredVariableReference? Builtin, WiredVariableAuthorization? Authorization);
}
