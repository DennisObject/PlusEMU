using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class OpenHelpToolComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.OpenHelpToolComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(0);
}
