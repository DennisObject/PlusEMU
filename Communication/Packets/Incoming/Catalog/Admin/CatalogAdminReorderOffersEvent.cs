using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogAdminReorderOffersEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogAdminReorderOffersEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (CatalogAdminPacketReader.ReorderOffers(packet) is not { } reorder)
        {
            session.Send(new CatalogAdminResultComposer(false, $"Reorder 1 to {CatalogAdminPacketReader.MaxReorderCount} offers at a time."));
            return Task.CompletedTask;
        }
        CatalogAdminResponder.Send(session, "reorder", reorder.Envelope, _catalogAdmin.ReorderOffers(session.GetHabbo(), reorder.Envelope, reorder.Orders));
        return Task.CompletedTask;
    }
}
