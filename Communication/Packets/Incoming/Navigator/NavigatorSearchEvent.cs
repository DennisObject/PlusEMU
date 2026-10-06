using Plus.Communication.Packets.Outgoing.Navigator.New;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal sealed class NavigatorSearchEvent(INavigatorSearchService searches) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new NavigatorSearchResultSetComposer(searches.Search(session, packet.ReadString(), packet.ReadString())));
        return Task.CompletedTask;
    }
}
