using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class UserHabbiconsComposer(HabbiconSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.UserHabbiconsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(snapshot.Items.Values.Count(item => item.Collected));
        foreach (var item in snapshot.Items.Values.Where(item => item.Collected))
        {
            packet.WriteInteger(item.Id);
            packet.WriteInteger(item.State);
        }
        packet.WriteInteger(snapshot.Recent.Count);
        foreach (var id in snapshot.Recent) packet.WriteInteger(id);
    }
}
