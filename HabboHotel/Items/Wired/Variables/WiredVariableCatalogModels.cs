namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed record WiredVariableDescription(WiredVariableDefinition Definition, bool HasValue, bool ReadOnly)
{
    public IReadOnlyDictionary<int, string> TextConnector { get; init; } = new Dictionary<int, string>();
    public bool IsBuiltin
    {
        get; init;
    }
    public bool IsDerived
    {
        get; init;
    }
    public bool CanCreateAndDelete => !ReadOnly && !IsBuiltin && Definition.Target is WiredVariableTarget.User or WiredVariableTarget.Furni;
    public bool CanWriteValue => !ReadOnly && HasValue;
    public bool CanReadTimestamps => !IsBuiltin && !IsDerived;
    // Catalog target/type codes differ from scalar editor target codes.
    public int CatalogTarget => Definition.Target switch
    {
        WiredVariableTarget.Global => 0,
        WiredVariableTarget.User => 1,
        WiredVariableTarget.Furni => 2,
        _ => 3
    };
    public string CatalogId => (Definition.Target switch
    {
        WiredVariableTarget.User => "user:",
        WiredVariableTarget.Furni => "furni:",
        WiredVariableTarget.Global => "room:",
        _ => "ctx:"
    }) + Definition.ItemId;
    public int Hash
    {
        get
        {
            var hash = StableHash(CatalogId);

            foreach (var value in new[] { StableHash(Definition.Name), CatalogTarget, (int)Definition.Availability, CatalogTarget,
                HasValue ? 1 : 0, TextConnector.Count > 0 ? 2 : 0, ReadOnly ? 4 : 0, IsBuiltin ? 1 : 0, IsDerived ? 1 : 0 })
            {
                hash = unchecked(hash * 31 + value);
            }

            foreach (var (key, value) in TextConnector.OrderBy(x => x.Key))
            {
                hash = unchecked(hash * 31 + key);
                hash = unchecked(hash * 31 + StableHash(value));
            }

            return hash;
        }
    }
    private static int StableHash(string text)
    {
        var hash = 0;

        foreach (var character in text)
        {
            hash = unchecked(hash * 31 + character);
        }

        return hash;
    }
}

/// <summary>A request-scoped, authorized catalog. Hashes are deterministic across processes.</summary>
public sealed class WiredVariableCatalog(IReadOnlyList<WiredVariableDescription> variables)
{
    public IReadOnlyList<WiredVariableDescription> Variables { get; } = variables;
    public int Hash => Variables.Aggregate(Variables.Count, (hash, variable) => unchecked(hash * 31 + variable.Hash));
    public WiredVariableDescription? Find(string id) => Variables.FirstOrDefault(x => x.CatalogId == id);
    public IReadOnlyList<WiredVariableCatalogDiff> Diff(IReadOnlyDictionary<string, int> known)
    {
        var current = Variables.ToDictionary(x => x.CatalogId);
        var removed = known.Keys.Where(x => !current.ContainsKey(x)).Order(StringComparer.Ordinal).ToArray();
        var changed = Variables.Where(x => !known.TryGetValue(x.CatalogId, out var hash) || x.Hash != hash).ToArray();
        var count = Math.Max(1, (changed.Length + 99) / 100);

        return Enumerable.Range(0, count).Select(i => new WiredVariableCatalogDiff(Hash, i == count - 1,
            i == 0 ? removed : [], changed.Skip(i * 100).Take(100).ToArray())).ToArray();
    }
}
public sealed record WiredVariableCatalogDiff(int Hash, bool LastChunk, IReadOnlyList<string> Removed,
    IReadOnlyList<WiredVariableDescription> Changed);
public sealed record WiredVariableMenuSnapshot(uint RoomId, IReadOnlyList<WiredVariableDescription> Definitions,
    IReadOnlyList<WiredVariableStoredHolder> Assignments);
public sealed record WiredVariableStoredHolder(WiredVariableKey Key, string Name, WiredVariableValue Value);
public sealed record WiredVariableHolderPage(int Total, int Page, int PageSize, IReadOnlyList<WiredVariableStoredHolder> Holders);

internal static class WiredVariablePaging
{
    public static WiredVariableHolderPage Page(IEnumerable<WiredVariableStoredHolder> values, int page, int size, int sort)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);
        var ordered = sort switch
        {
            0 => values.OrderBy(x => x.Value.Value).ThenBy(x => x.Key.HolderId),
            1 => values.OrderByDescending(x => x.Value.Value).ThenBy(x => x.Key.HolderId),
            2 => values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key.HolderId),
            _ => values.OrderBy(x => x.Key.HolderId)
        };
        var all = ordered.ToArray();
        var offset = (int)Math.Min((long)(page - 1) * size, all.Length);

        return new(all.Length, page, size, all.Skip(offset).Take(size).ToArray());
    }
}
