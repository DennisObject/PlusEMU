using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class UserHabbiconStatusChangedComposer(int id, int state) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.UserHabbiconStatusChangedComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(id);
        packet.WriteInteger(state);
    }
}
