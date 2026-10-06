using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class InitializeNewNavigatorEvent(INavigatorPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        presentation.InitializeNewNavigator(session);

        return Task.CompletedTask;
    }
}
