using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Users;

internal class OpenPlayerProfileEvent(IPlayerProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        packet.ReadBool(); // Profile request flag (ignored by this handler).

        return profiles.Open(session, userId);
    }
}
