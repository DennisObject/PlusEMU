using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Ambassadors;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class AmbassadorAlertEvent(IAmbassadorsManager ambassadors) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => ambassadors.Warn(session, packet.ReadInt(), "Alert");
}
