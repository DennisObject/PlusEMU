using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Game;

/// <summary>Reads a fixed SnowStorm payload; truncated packets and trailing bytes are rejected instead of thrown.</summary>
internal static class SnowStormPacketReader
{
    public static bool TryReadInts(IIncomingPacket packet, int count, out int[] values)
    {
        values = new int[count];

        try {
            for (var index = 0; index < count; index++) {
                values[index] = packet.ReadInt();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException) {
            return false;
        }

        return !packet.HasDataRemaining();
    }

    public static bool TryReadString(IIncomingPacket packet, out string value)
    {
        try {
            value = packet.ReadString();
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException) {
            value = string.Empty;

            return false;
        }

        return !packet.HasDataRemaining();
    }

    /// <summary>AIR sends an optional boolean (true) with ExitGame; anything else after it is rejected.</summary>
    public static bool TryReadOptionalBool(IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) {
            return true;
        }

        try {
            packet.ReadBool();
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException) {
            return false;
        }

        return !packet.HasDataRemaining();
    }
}
