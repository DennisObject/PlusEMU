using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class MessengerInitComposer(int limit) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.MessengerInitComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(limit); packet.WriteInteger(limit); packet.WriteInteger(limit); packet.WriteInteger(0);
    }
}