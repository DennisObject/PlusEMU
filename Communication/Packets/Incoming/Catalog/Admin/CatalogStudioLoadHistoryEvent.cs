using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogStudioLoadHistoryEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogStudioLoadHistoryEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); // draft version: PlusEMU has only the live one
        int offset = packet.ReadInt(), limit = packet.ReadInt();
        CatalogAdminResponder.Read(session, () => new CatalogStudioHistoryComposer(_catalogAdmin.History(session.GetHabbo(), offset, limit)));
        return Task.CompletedTask;
    }
}
