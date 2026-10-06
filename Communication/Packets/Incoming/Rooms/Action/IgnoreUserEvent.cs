using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Ignores;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class IgnoreUserEvent(IPlayerIgnoreService ignores) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => ignores.Ignore(session, packet.ReadString());
}
