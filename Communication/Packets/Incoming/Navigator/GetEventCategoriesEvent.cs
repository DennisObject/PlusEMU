using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class GetEventCategoriesEvent(INavigatorPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        presentation.ShowEventCategories(session);
        return Task.CompletedTask;
    }
}
