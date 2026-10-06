using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Rooms.Avatar;

internal class ChangeMottoEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        profiles.ChangeMotto(session, packet.ReadString());
        return Task.CompletedTask;
    }
}
