using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Ignores;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class UnignoreUserEvent(IPlayerIgnoreService ignores) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => ignores.Unignore(session, packet.ReadString());
}
