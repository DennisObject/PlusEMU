using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Preferences;

internal class SetUIFlagsEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var value = packet.ReadInt();
        profiles.SetFriendBarState(session, value);

        return Task.CompletedTask;
    }
}
