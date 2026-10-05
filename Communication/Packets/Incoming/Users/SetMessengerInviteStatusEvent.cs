using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Users;

internal class SetMessengerInviteStatusEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        profiles.SetMessengerInvitePreference(session, packet.ReadBool());
}
