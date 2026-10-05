using System.Globalization;
using System.Collections.Immutable;
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
    private readonly WiredVariableCatalogDiff _captured = diff with { Removed = diff.Removed.ToImmutableArray(), Changed = diff.Changed.Select(WiredVariableWireCapture.Capture).ToImmutableArray() };
    public uint MessageId => ServerPacketHeader.WiredAllVariablesDiffComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_captured.Hash); packet.WriteBoolean(_captured.LastChunk);
        packet.WriteInteger(_captured.Removed.Count);
        foreach (var id in _captured.Removed) packet.WriteString(id);
        packet.WriteInteger(_captured.Changed.Count);
        foreach (var variable in _captured.Changed) { packet.WriteInteger(variable.Hash); WriteVariable(packet, variable); }
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
    private readonly WiredVariableDescription _variable = WiredVariableWireCapture.Capture(variable);
    private readonly ImmutableArray<WiredVariableStoredHolder> _holders = holders.ToImmutableArray();
    public uint MessageId => ServerPacketHeader.WiredVariableHoldersComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(roomId); WiredAllVariablesDiffComposer.WriteVariable(packet, _variable);
        packet.WriteInteger(_holders.Length);
        foreach (var holder in _holders) { packet.WriteInteger(checked((int)holder.Key.HolderId)); packet.WriteInteger(holder.Value.Value); }
    }
}

public sealed class WiredVariableHoldersPageComposer(string variableId, WiredVariableHolderPage page, int userFilter, int sort) : IServerPacket
{
    private readonly WiredVariableHolderPage _captured = page with { Holders = page.Holders.ToImmutableArray() };
    public uint MessageId => ServerPacketHeader.WiredVariableHoldersPageComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(variableId); packet.WriteInteger(_captured.Total); packet.WriteInteger(_captured.Page); packet.WriteInteger(_captured.PageSize);
        packet.WriteInteger(_captured.Holders.Count);
        foreach (var holder in _captured.Holders)
        {
            packet.WriteInteger(holder.Key.Target switch { WiredVariableTarget.Global => 0, WiredVariableTarget.User => 1, WiredVariableTarget.Furni => 2, _ => 3 });
            packet.WriteInteger(checked((int)holder.Key.HolderId)); packet.WriteString(holder.Name); packet.WriteInteger(holder.Value.Value);
            WriteTimestamp(packet, holder.Value.CreatedAt); WriteTimestamp(packet, holder.Value.UpdatedAt);
        }
        packet.WriteInteger(userFilter); packet.WriteInteger(sort);
    }
    internal static void WriteTimestamp(IOutgoingPacket packet, DateTimeOffset? timestamp)
    {
        var milliseconds = timestamp?.ToUnixTimeMilliseconds() ?? 0;
        packet.WriteInteger(unchecked((int)(milliseconds >> 32))); packet.WriteInteger(unchecked((int)milliseconds));
        packet.WriteString(timestamp?.UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? "");
    }
}
