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
    WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update, WiredVariableAuthorization? authorization = null);
    IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId);
    int DeleteDefinition(uint definitionId);
}
