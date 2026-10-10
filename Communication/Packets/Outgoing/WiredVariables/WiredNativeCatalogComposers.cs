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
            variable.Write(packet, true);
        }
    }
}
