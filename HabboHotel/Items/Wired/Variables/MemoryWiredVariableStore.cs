namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Active room/execution state. It deliberately has no hydration or database fallback.</summary>
public sealed class MemoryWiredVariableStore(MemoryWiredVariableStore? parent = null) : IWiredVariableStore
{
    private readonly object _gate = parent?._gate ?? new();
    private readonly Dictionary<WiredVariableKey, WiredVariableValue> _values = [];
    public MemoryWiredVariableStore? Parent { get; } = parent;
    public MemoryWiredVariableStore CreateChild() => new(this);
    internal MemoryWiredVariableStore? Owner(WiredVariableKey key)
    {
        lock (_gate) {
            for (var scope = this; scope != null; scope = scope.Parent) {
                if (scope._values.ContainsKey(key)) {
                    return scope;
                }
            }

            return null;
        }
    }
    public WiredVariableValue? Read(WiredVariableKey key)
    {
        lock (_gate) {
            return Owner(key)?._values.GetValueOrDefault(key);
        }
    }
    public WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update, WiredVariableAuthorization? authorization = null)
    {
        lock (_gate) {
            var before = _values.GetValueOrDefault(key);
            var after = update(before);

            if (after is null) {
                _values.Remove(key);
            }
            else {
                _values[key] = after;
            }

            return new(before, after);
        }
    }
    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId)
    {
        lock (_gate) {
            var visible = Parent?.GetHolders(definitionId).ToDictionary() ?? new Dictionary<WiredVariableKey, WiredVariableValue>();

            foreach (var (key, value) in _values.Where(x => x.Key.DefinitionId == definitionId)) {
                visible[key] = value;
            }

            return visible;
        }
    }
    public int DeleteDefinition(uint definitionId)
    {
        lock (_gate) {
            var keys = _values.Keys.Where(x => x.DefinitionId == definitionId).ToArray();

            foreach (var key in keys) {
                _values.Remove(key);
            }

            return keys.Length;
        }
    }
    public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> ClearValues(uint definitionId, WiredVariableAuthorization authorization)
    {
        lock (_gate) {
            var removed = _values.Where(x => x.Key.DefinitionId == definitionId).ToDictionary();

            foreach (var key in removed.Keys) {
                _values.Remove(key);
            }

            return removed;
        }
    }
    public void RemoveHolder(WiredVariableTarget target, long holderId)
    {
        lock (_gate) {
            foreach (var key in _values.Keys.Where(x => x.Target == target && x.HolderId == holderId).ToArray()) {
                _values.Remove(key);
            }
        }
    }
}
