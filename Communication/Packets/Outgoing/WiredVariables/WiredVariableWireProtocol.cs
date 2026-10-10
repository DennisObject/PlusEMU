using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

// Format is chosen by each explicit request, never by a connection capability.
public static class WiredVariableWireProtocol
{
    public const int Version = 1;
    public static bool CanSend(IEnumerable<WiredVariableStoredHolder> values, bool exact = false) =>
        exact || values.All(value => value.Value.Value is >= int.MinValue and <= int.MaxValue);

    public static void WriteValue(IOutgoingPacket packet, long value, bool exact)
    {
        if (exact) {
            packet.WriteInt(unchecked((int)(value >> 32)));
            packet.WriteInt(unchecked((int)value));
        }
        else {
            packet.WriteInt(checked((int)value));
        }
    }
}
