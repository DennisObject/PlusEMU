using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

internal static class CatalogAdminResponder
{
    // The editor matches these acknowledgements to its open form by operation id; other actions get success and message.
    private static readonly HashSet<string> SmartSaveActions = ["createPage", "savePage", "createOffer", "saveOffer"];

    public static void Send(GameClient session, string action, CatalogAdminEnvelope envelope, CatalogAdminOutcome outcome)
    {
        CatalogAdminSmartSave? smartSave = null;
        if (SmartSaveActions.Contains(action) && envelope.OperationId.Length is > 0 and <= CatalogAdminEnvelope.MaxOperationIdLength)
            smartSave = new(envelope.OperationId, action, outcome, session.GetHabbo().Username);
        session.Send(new CatalogAdminResultComposer(outcome.Success, outcome.Message, smartSave));
    }

    public static void Read(GameClient session, Func<IServerPacket> read)
    {
        try
        {
            session.Send(read());
        }
        catch (CatalogAdminRejected rejected)
        {
            session.Send(new CatalogAdminResultComposer(false, rejected.Message));
        }
    }
}
