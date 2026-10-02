namespace Plus.HabboHotel.Items.Wired.Variables;

// These are Octane's scalar editor codes, not the unrelated array target codes.
public enum WiredVariableTarget { User = 0, Furni = 1, Context = 2, Global = 3 }
public enum WiredVariableAvailability { UserActive = 0, RoomActive = 1, Persistent = 10, Shared = 11 }
public enum WiredVariableMutation { Give, Replace, Set, Remove }
public enum WiredVariableChangeKind { Created, Updated, Removed }

public sealed record WiredVariableReference(WiredVariableTarget Target, string Token);
public sealed record WiredVariableLink(uint SourceRoomId, WiredVariableReference Source, bool ReadOnly);

public sealed record WiredVariableDefinition(uint ItemId, uint RoomId, uint OwnerId, string Name,
    WiredVariableTarget Target, WiredVariableAvailability Availability, bool HasValue, int InitialValue = 0,
    WiredVariableLink? Link = null)
{
    public string Token => $"custom:{ItemId}";
    public bool IsDurable => Availability is WiredVariableAvailability.Persistent or WiredVariableAvailability.Shared;
}

/// <summary>The directory must report current item placement and room ownership, including unloaded rooms.</summary>
public interface IWiredVariableDirectory
{
    WiredVariableDefinition? Find(uint itemId);
    uint? GetRoomOwner(uint roomId);
}
