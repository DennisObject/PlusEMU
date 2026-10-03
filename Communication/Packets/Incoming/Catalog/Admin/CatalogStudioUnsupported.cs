using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

// Studio requests PlusEMU does not implement still get an answer in the shape the client waits for, so it settles.
internal static class CatalogStudioUnsupported
{
    public static (string Code, string Message, int Revision) Answer(ICatalogAdminService catalogAdmin, GameClient session, string feature)
    {
        try
        {
            return (CatalogAdminCodes.Unsupported, $"{feature} is not supported by this hotel.", catalogAdmin.Revision(session.GetHabbo()));
        }
        catch (CatalogAdminRejected rejected)
        {
            return (rejected.Code, rejected.Message, 0);
        }
    }
}
