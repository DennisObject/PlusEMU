using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Users;

public class KickbackInfoComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.KickbackInfoComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Club kickback is not tracked. Zeros keep the HC center parser aligned.
        packet.WriteInteger(0);
        packet.WriteString(string.Empty);
        packet.WriteDouble(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
    }
}
