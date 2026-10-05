using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Avatar;

internal class CheckUserNameEvent(IUserNameService names) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        names.Check(session, packet.ReadString());
}
