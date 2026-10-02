using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public sealed class MessengerMessageFailedComposer(int confirmationId, int errorCode) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.MessengerMessageFailedComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(confirmationId);
        packet.WriteInteger(errorCode);
    }
}
