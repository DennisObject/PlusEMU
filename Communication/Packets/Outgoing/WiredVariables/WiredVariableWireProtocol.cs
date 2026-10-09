using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

// Capability belongs to this connection, and survives automatic editor refreshes.
public static class WiredVariableWireProtocol
{
    public const int Version = 1;
    private sealed class Capability { }
    private static readonly ConditionalWeakTable<GameClient, Capability> Exact = new();
    public static bool IsExact(GameClient client) => Exact.TryGetValue(client, out _);
    public static void Enable(GameClient client) => Exact.GetValue(client, _ => new());
    public static bool TryReadRequestVersion(IIncomingPacket packet, out bool exact)
    {
        exact = false;

        try {
            if (!packet.HasDataRemaining()) {
                return true;
            }

            if (packet.ReadInt() != Version || packet.HasDataRemaining()) {
                return false;
            }

            exact = true;

            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException) {
            return false;
        }
    }
    public static bool CanSend(GameClient client, IEnumerable<WiredVariableStoredHolder> values)
    {
        if (IsExact(client) || values.All(value => value.Value.Value is >= int.MinValue and <= int.MaxValue)) {
            return true;
        }

        client.SendNotification("This variable contains a 64-bit value. Update the client to inspect it exactly.");

        return false;
    }
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
