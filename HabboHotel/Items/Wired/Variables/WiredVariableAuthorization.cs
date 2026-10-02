using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>The exact ownership and reference lineage resolved before a durable mutation.</summary>
public sealed record WiredVariableAuthorization(uint RequestRoomId, uint OwnerId, ImmutableArray<WiredVariableDefinition> Lineage)
{
    public bool IsCurrent(IWiredVariableDirectory directory) => directory.GetRoomOwner(RequestRoomId) == OwnerId
        && Lineage.All(expected => directory.GetRoomOwner(expected.RoomId) == OwnerId && directory.Find(expected.ItemId) == expected);
}
