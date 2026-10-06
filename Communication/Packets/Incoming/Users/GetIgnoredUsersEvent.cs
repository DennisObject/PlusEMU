using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Users;

internal sealed class GetIgnoredUsersEvent(IUserSocialShowcaseService social) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        social.ShowIgnoredUsers(session);
}
