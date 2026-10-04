using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

// Reads Octane's catalog editor packets (Octane-Renderer outgoing/catalog/CatalogAdmin*Composer), field by field.
internal static class CatalogAdminPacketReader
{
    public const int MaxReorderCount = 500;

    // Every mutation packet ends with: draftVersionId, expectedRevision, lockToken, summary, operationId.
    public static CatalogAdminEnvelope Envelope(IIncomingPacket packet, string catalogType) =>
        new(catalogType, packet.ReadInt(), packet.ReadInt(), packet.ReadString(), packet.ReadString(), packet.ReadString());

    public static (CatalogAdminPage Page, CatalogAdminEnvelope Envelope) SavePage(IIncomingPacket packet)
    {
        int pageId = packet.ReadInt();
        string caption = packet.ReadString(), captionSave = packet.ReadString(), layout = packet.ReadString();
        int iconImage = packet.ReadInt();
        string requiredPermission = packet.ReadString();
        bool visible = packet.ReadBool(), enabled = packet.ReadBool();
        int orderNum = packet.ReadInt(), parentId = packet.ReadInt();
        string headline = packet.ReadString(), teaser = packet.ReadString(), textDetails = packet.ReadString();
        string targetType = packet.ReadString(), catalogMode = packet.ReadString(), text1 = packet.ReadString();
        int iconColor = packet.ReadInt();
        bool clubOnly = packet.ReadBool();
        string special = packet.ReadString(), text2 = packet.ReadString(), textTeaser = packet.ReadString();
        int roomId = packet.ReadInt();
        string includes = packet.ReadString();
        var type = CatalogType(targetType);
        var page = new CatalogAdminPage(type, pageId, parentId, captionSave, caption, layout, iconColor, iconImage, requiredPermission, orderNum,
            visible, enabled, clubOnly, catalogMode, headline, teaser, special, text1, text2, textDetails, textTeaser, roomId, includes);
        return (page, Envelope(packet, type));
    }

    public static (CatalogAdminPage Page, CatalogAdminEnvelope Envelope) CreatePage(IIncomingPacket packet)
    {
        string caption = packet.ReadString(), captionSave = packet.ReadString(), layout = packet.ReadString();
        int iconImage = packet.ReadInt();
        string requiredPermission = packet.ReadString();
        bool visible = packet.ReadBool(), enabled = packet.ReadBool();
        int orderNum = packet.ReadInt(), parentId = packet.ReadInt();
        string targetType = packet.ReadString(), catalogMode = packet.ReadString();
        int iconColor = packet.ReadInt();
        bool clubOnly = packet.ReadBool();
        string headline = packet.ReadString(), teaser = packet.ReadString(), special = packet.ReadString();
        string text1 = packet.ReadString(), text2 = packet.ReadString(), textDetails = packet.ReadString(), textTeaser = packet.ReadString();
        int roomId = packet.ReadInt();
        string includes = packet.ReadString();
        var type = CatalogType(targetType);
        var page = new CatalogAdminPage(type, 0, parentId, captionSave, caption, layout, iconColor, iconImage, requiredPermission, orderNum,
            visible, enabled, clubOnly, catalogMode, headline, teaser, special, text1, text2, textDetails, textTeaser, roomId, includes);
        return (page, Envelope(packet, type));
    }

    // Save sends the offer id first; create does not.
    public static (CatalogAdminOffer Offer, CatalogAdminEnvelope Envelope) Offer(IIncomingPacket packet, bool hasOfferId)
    {
        int offerId = hasOfferId ? packet.ReadInt() : 0;
        int pageId = packet.ReadInt();
        string itemIds = packet.ReadString(), catalogName = packet.ReadString();
        int costCredits = packet.ReadInt(), costPoints = packet.ReadInt(), pointsType = packet.ReadInt(), amount = packet.ReadInt();
        int clubOnly = packet.ReadInt();
        string extradata = packet.ReadString();
        bool haveOffer = packet.ReadBool();
        int offerIdGroup = packet.ReadInt(), limitedStack = packet.ReadInt(), orderNumber = packet.ReadInt(), songId = packet.ReadInt();
        var type = CatalogType(packet.ReadString());
        var offer = new CatalogAdminOffer(type, offerId, itemIds, pageId, catalogName, costCredits, costPoints, pointsType, amount,
            limitedStack, orderNumber, offerIdGroup, songId, extradata, haveOffer, clubOnly != 0);
        return (offer, Envelope(packet, type));
    }

    // Delete page/offer: id, catalog type, envelope.
    public static (int Id, CatalogAdminEnvelope Envelope) Target(IIncomingPacket packet)
    {
        int id = packet.ReadInt();
        return (id, Envelope(packet, CatalogType(packet.ReadString())));
    }

    // Page enabled/visible: page id, flag, catalog type, envelope.
    public static (int PageId, bool Value, CatalogAdminEnvelope Envelope) PageFlag(IIncomingPacket packet)
    {
        int pageId = packet.ReadInt();
        bool value = packet.ReadBool();
        return (pageId, value, Envelope(packet, CatalogType(packet.ReadString())));
    }

    // null when the count is out of range; the rest of the packet is then not read.
    public static (List<(int OfferId, int OrderNumber)> Orders, CatalogAdminEnvelope Envelope)? ReorderOffers(IIncomingPacket packet)
    {
        int count = packet.ReadInt();
        if (count is < 1 or > MaxReorderCount)
            return null;
        var orders = new List<(int, int)>(count);
        for (int i = 0; i < count; i++)
            orders.Add((packet.ReadInt(), packet.ReadInt()));
        return (orders, Envelope(packet, CatalogType(packet.ReadString())));
    }

    public static string CatalogType(string value) => CatalogAdminTypes.Parse(value) ?? CatalogAdminTypes.Normal;
}
