using System.Globalization;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

public sealed class WiredAllVariablesHashComposer(int hash) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredAllVariablesHashComposer;
    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(hash);
}

public sealed class WiredAllVariablesDiffComposer(WiredVariableCatalogDiff diff) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredAllVariablesDiffComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(diff.Hash); packet.WriteBoolean(diff.LastChunk);
        packet.WriteInteger(diff.Removed.Count);
        foreach (var id in diff.Removed) packet.WriteString(id);
        packet.WriteInteger(diff.Changed.Count);
        foreach (var variable in diff.Changed) { packet.WriteInteger(variable.Hash); WriteVariable(packet, variable); }
    }
    internal static void WriteVariable(IOutgoingPacket packet, WiredVariableDescription variable)
    {
        packet.WriteString(variable.CatalogId); packet.WriteInteger(variable.CatalogTarget); packet.WriteString(variable.Definition.Name);
        packet.WriteInteger((int)variable.Definition.Availability); packet.WriteInteger(variable.CatalogTarget);
        packet.WriteBoolean(true); packet.WriteBoolean(variable.CanCreateAndDelete); packet.WriteBoolean(variable.HasValue);
        packet.WriteBoolean(variable.CanWriteValue); packet.WriteBoolean(false); packet.WriteBoolean(false);
        packet.WriteBoolean(variable.CanReadTimestamps); packet.WriteBoolean(variable.CanReadTimestamps);
        packet.WriteBoolean(variable.TextConnector.Count > 0);
        if (variable.TextConnector.Count == 0) return;
        packet.WriteInteger(variable.TextConnector.Count);
        foreach (var (key, value) in variable.TextConnector.OrderBy(x => x.Key)) { packet.WriteInteger(key); packet.WriteString(value); }
    }
}

public sealed class WiredVariableHoldersComposer(uint roomId, WiredVariableDescription variable,
    IReadOnlyList<WiredVariableStoredHolder> holders) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredVariableHoldersComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(roomId); WiredAllVariablesDiffComposer.WriteVariable(packet, variable);
        packet.WriteInteger(holders.Count);
        foreach (var holder in holders) { packet.WriteInteger(checked((int)holder.Key.HolderId)); packet.WriteInteger(holder.Value.Value); }
    }
}

public sealed class WiredVariableHoldersPageComposer(string variableId, WiredVariableHolderPage page, int userFilter, int sort) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredVariableHoldersPageComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(variableId); packet.WriteInteger(page.Total); packet.WriteInteger(page.Page); packet.WriteInteger(page.PageSize);
        packet.WriteInteger(page.Holders.Count);
        foreach (var holder in page.Holders)
        {
            packet.WriteInteger(holder.Key.Target switch { WiredVariableTarget.Global => 0, WiredVariableTarget.User => 1, WiredVariableTarget.Furni => 2, _ => 3 });
            packet.WriteInteger(checked((int)holder.Key.HolderId)); packet.WriteString(holder.Name); packet.WriteInteger(holder.Value.Value);
            WriteTimestamp(packet, holder.Value.CreatedAtMs); WriteTimestamp(packet, holder.Value.UpdatedAtMs);
        }
        packet.WriteInteger(userFilter); packet.WriteInteger(sort);
    }
    private static void WriteTimestamp(IOutgoingPacket packet, long timestamp)
    {
        packet.WriteInteger(unchecked((int)(timestamp >> 32))); packet.WriteInteger(unchecked((int)timestamp));
        packet.WriteString(timestamp > 0 && timestamp <= 253402300799999L
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) : "");
    }
}
