using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class GetGroupFurniConfigEvent(IGroupPresentationService groups) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        groups.ShowCatalogFurnitureConfiguration(session);
        return Task.CompletedTask;
    }
}
