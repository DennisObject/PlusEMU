using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Users;

internal class SetUserFocusPreferenceEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        profiles.SetFocusPreference(session, packet.ReadBool());
        return Task.CompletedTask;
    }
}
