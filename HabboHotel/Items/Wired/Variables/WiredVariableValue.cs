namespace Plus.HabboHotel.Items.Wired.Variables;

public readonly record struct WiredVariableKey(uint DefinitionId, WiredVariableTarget Target, long HolderId);
public sealed record WiredVariableValue(int Value, long CreatedAtMs, long UpdatedAtMs);
public sealed record WiredVariableWrite(WiredVariableValue? Before, WiredVariableValue? After)
{
    public bool Changed => Before != After;
}
public sealed record WiredVariableChange(uint RoomId, WiredVariableKey Key, WiredVariableChangeKind Kind,
    WiredVariableValue? Before, WiredVariableValue? After, int EntityId, int Depth);

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
}
