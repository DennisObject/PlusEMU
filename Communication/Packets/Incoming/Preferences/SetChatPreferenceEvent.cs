using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Preferences;

internal class SetChatPreferenceEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => profiles.SetChatPreference(session, packet.ReadBool());
}
