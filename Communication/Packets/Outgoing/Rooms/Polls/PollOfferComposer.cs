using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Polls;

namespace Plus.Communication.Packets.Outgoing.Rooms.Polls;

public sealed class PollOfferComposer(RoomPollSnapshot poll) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PollOfferComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(poll.Id);
        packet.WriteString(poll.Type);
        packet.WriteString(poll.Title);
        packet.WriteString(poll.Summary);
    }
}
