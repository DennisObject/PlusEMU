using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.Communication.Packets.Incoming.Handshake;

[NoAuthenticationRequired]
public class SSOTicketEvent(ISsoLoginService login) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => login.Login(session, packet.ReadString());
}
