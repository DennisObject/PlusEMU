namespace Plus.HabboHotel.Items.Wired.Variables;

public readonly record struct WiredVariableKey(uint DefinitionId, WiredVariableTarget Target, long HolderId);
public sealed record WiredVariableValue
{
    private DateTimeOffset? _createdAt;
    private DateTimeOffset? _updatedAt;

    public WiredVariableValue(long value, DateTimeOffset? createdAt, DateTimeOffset? updatedAt)
    {
        Value = value;
        CreatedAt = createdAt?.ToUniversalTime();
        UpdatedAt = updatedAt?.ToUniversalTime();
    }

    public long Value { get; init; }
    public DateTimeOffset? CreatedAt { get => _createdAt; init => _createdAt = value?.ToUniversalTime(); }
    public DateTimeOffset? UpdatedAt { get => _updatedAt; init => _updatedAt = value?.ToUniversalTime(); }
}
public sealed record WiredVariableWrite(WiredVariableValue? Before, WiredVariableValue? After)
{
    public bool Changed => Before != After;
}
public sealed record WiredVariableChange(uint RoomId, WiredVariableKey Key, WiredVariableChangeKind Kind,
    WiredVariableValue? Before, WiredVariableValue? After, int EntityId, int Depth)
{
    public int Origin { get; init; } // 0 Wired, 1 API, 2 creator tools; active editor mask bits.
    public string InternalKey { get; init; } = "";
}

/// <summary>Mutate is atomic, including when no value exists. Failure throws before a change is published.</summary>
public interface IWiredVariableStore
{
    WiredVariableValue? Read(WiredVariableKey key);
    IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> ReadMany(IReadOnlyCollection<WiredVariableKey> keys)
    {
        var requested = keys.ToHashSet();

        return keys.Select(x => x.DefinitionId).Distinct().SelectMany(GetHolders)
            .Where(x => requested.Contains(x.Key)).ToDictionary();
    }
    WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update, WiredVariableAuthorization? authorization = null);
    IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId);
    WiredVariableHolderPage ReadPage(uint definitionId, WiredVariableTarget target, int page, int size, int sort,
        IReadOnlyCollection<long>? holderFilter = null, IReadOnlyDictionary<long, string>? names = null) =>
        WiredVariablePaging.Page(GetHolders(definitionId).Where(x => x.Key.Target == target && (holderFilter is null || holderFilter.Contains(x.Key.HolderId)))
            .Select(x => new WiredVariableStoredHolder(x.Key, names?.GetValueOrDefault(x.Key.HolderId) ?? x.Key.HolderId.ToString(System.Globalization.CultureInfo.InvariantCulture), x.Value)), page, size, sort);
    int DeleteDefinition(uint definitionId);
    IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> ClearValues(uint definitionId, WiredVariableAuthorization authorization) =>
        throw new NotSupportedException("This store does not support atomic value clearing.");
}
