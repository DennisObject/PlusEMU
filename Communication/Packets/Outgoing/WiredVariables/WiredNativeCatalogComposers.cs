using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

public sealed class WiredNativeCatalogDiffComposer(WiredNativeCatalogDiff diff) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredAllVariablesDiffComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(diff.Hash);
        packet.WriteBoolean(diff.LastChunk);
        packet.WriteInteger(diff.Removed.Length);

        foreach (var id in diff.Removed) {
            packet.WriteString(id);
        }

        packet.WriteInteger(diff.Changed.Length);

        foreach (var variable in diff.Changed) {
            packet.WriteInteger(variable.Hash);
            packet.WriteString(variable.Id);
            packet.WriteInteger(variable.Type);
            packet.WriteString(variable.Name);
            packet.WriteInteger(variable.Availability);
            packet.WriteInteger(variable.Target);
            packet.WriteBoolean(variable.AlwaysAvailable);
            packet.WriteBoolean(variable.CanCreateAndDelete);
            packet.WriteBoolean(variable.HasValue);
            packet.WriteBoolean(variable.CanWriteValue);
            packet.WriteBoolean(variable.CanInterceptChanges);
            packet.WriteBoolean(variable.IsInvisible);
            packet.WriteBoolean(variable.CanReadCreationTime);
            packet.WriteBoolean(variable.CanReadLastUpdateTime);
            packet.WriteBoolean(variable.Connector.HasValue);

            if (variable.Connector is { } connector) {
                packet.WriteInteger(connector.Length);

                foreach (var pair in connector) {
                    packet.WriteInteger(pair.Key);
                    packet.WriteString(pair.Value);
                }
            }
        }
    }
}
