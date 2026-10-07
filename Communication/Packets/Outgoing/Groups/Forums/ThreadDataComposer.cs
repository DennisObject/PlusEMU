using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums;

public sealed class ThreadDataComposer(ForumMessagesPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ThreadDataComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(page.GroupId);
        packet.WriteInteger(page.ThreadId);
        packet.WriteInteger(page.Start);
        packet.WriteInteger(page.Messages.Length);
        foreach (var message in page.Messages) {
            ForumWire.Message(packet, message);
        }
    }
}
