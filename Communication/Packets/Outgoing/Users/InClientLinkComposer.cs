using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Users;

public sealed class InClientLinkComposer(string link) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.InClientLinkComposer;
    public void Compose(IOutgoingPacket packet) => packet.WriteString(link);
}
