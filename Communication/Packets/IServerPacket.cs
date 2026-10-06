using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets;

public interface IServerPacket
{
    uint MessageId { get; }
    void Compose(IOutgoingPacket packet);
}
