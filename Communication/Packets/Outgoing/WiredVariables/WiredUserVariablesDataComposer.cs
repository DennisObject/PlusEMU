using System.Text.Json;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

/// <summary>Legacy editor snapshot of live room assignments; offline holders are available through the paged protocol.</summary>
public sealed class WiredUserVariablesDataComposer(WiredVariableMenuSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredUserVariablesDataComposer;
    public void Compose(IOutgoingPacket packet)
    {
        var definitions = snapshot.Definitions.ToDictionary(x => x.Definition.ItemId);
        packet.WriteUInteger(snapshot.RoomId);
        WriteDefinitions(WiredVariableTarget.User); WriteHolders(WiredVariableTarget.User);
        WriteDefinitions(WiredVariableTarget.Furni); WriteHolders(WiredVariableTarget.Furni);
        WriteDefinitions(WiredVariableTarget.Global);
        var globals = snapshot.Assignments.Where(x => x.Key.Target == WiredVariableTarget.Global).ToArray();
        packet.WriteInteger(globals.Length); foreach (var value in globals) WriteAssignment(value);
        WriteDefinitions(WiredVariableTarget.Context);
        var connectors = snapshot.Definitions.Where(x => x.TextConnector.Count > 0).Select(x => new
        {
            itemId = x.Definition.ItemId,
            variableType = x.Definition.Target switch { WiredVariableTarget.Furni => 0, WiredVariableTarget.Global => 1, WiredVariableTarget.User => 2, _ => 3 },
            textConnector = x.TextConnector.OrderBy(x => x.Key).Select(x => new { key = x.Key, value = x.Value }).ToArray()
        }).ToArray();
        if (connectors.Length > 0) packet.WriteString(JsonSerializer.Serialize(connectors));

        void WriteDefinitions(WiredVariableTarget target)
        {
            var selected = snapshot.Definitions.Where(x => x.Definition.Target == target).ToArray();
            packet.WriteInteger(selected.Length);
            foreach (var variable in selected)
            {
                packet.WriteUInteger(variable.Definition.ItemId); packet.WriteString(variable.Definition.Name); packet.WriteBoolean(variable.HasValue);
                packet.WriteInteger((int)variable.Definition.Availability); packet.WriteBoolean(variable.TextConnector.Count > 0); packet.WriteBoolean(variable.ReadOnly);
            }
        }
        void WriteHolders(WiredVariableTarget target)
        {
            var groups = snapshot.Assignments.Where(x => x.Key.Target == target).GroupBy(x => x.Key.HolderId).OrderBy(x => x.Key).ToArray();
            packet.WriteInteger(groups.Length);
            foreach (var group in groups)
            {
                packet.WriteInteger(checked((int)group.Key)); packet.WriteInteger(group.Count());
                foreach (var value in group.OrderBy(x => x.Key.DefinitionId)) WriteAssignment(value);
            }
        }
        void WriteAssignment(WiredVariableStoredHolder value)
        {
            var definition = definitions[value.Key.DefinitionId];
            packet.WriteUInteger(value.Key.DefinitionId); packet.WriteBoolean(definition.HasValue);
            packet.WriteInteger(definition.HasValue ? value.Value.Value : 0);
            packet.WriteInteger((int)Math.Clamp(value.Value.CreatedAtMs / 1000, 0, int.MaxValue));
            packet.WriteInteger((int)Math.Clamp(value.Value.UpdatedAtMs / 1000, 0, int.MaxValue));
        }
    }
}
