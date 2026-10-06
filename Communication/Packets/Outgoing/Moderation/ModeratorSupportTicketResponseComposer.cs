using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class ModeratorSupportTicketResponseComposer : IServerPacket
{
    private readonly SupportTicketResult _result;
    public uint MessageId => ServerPacketHeader.ModeratorSupportTicketResponseComposer;

    public ModeratorSupportTicketResponseComposer(SupportTicketResult result)
    {
        _result = result;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)_result);
        packet.WriteString("");
    }
}
