using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Handshake;

public class InfoRetrieveEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        profiles.ShowUserObject(session);
        return Task.CompletedTask;
    }
}
