using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class UpdateNavigatorSettingsEvent(INavigatorManager navigator) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => navigator.SaveHomeRoom(session, packet.ReadUInt());
}
