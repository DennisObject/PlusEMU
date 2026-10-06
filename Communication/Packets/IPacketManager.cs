using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets;

public interface IPacketManager
{
    Task TryExecutePacket(GameClient session, uint messageId, IIncomingPacket packet);
}
