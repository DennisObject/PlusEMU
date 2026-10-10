using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// Field order of Volt-Renderer's CatalogAdminPageDetailsMessageParser.
public sealed class CatalogAdminPageDetailsComposer : IServerPacket
{
    private readonly CatalogAdminPage _page;

    public uint MessageId => ServerPacketHeader.CatalogAdminPageDetailsComposer;

    public CatalogAdminPageDetailsComposer(CatalogAdminPage page) => _page = page;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_page.PageId);
        packet.WriteString(_page.Caption);
        packet.WriteString(_page.CaptionSave);
        packet.WriteInteger(_page.ParentId);
        packet.WriteString(_page.CatalogMode);
        packet.WriteString(_page.PageLayout);
        packet.WriteInteger(_page.IconColor);
        packet.WriteInteger(_page.IconImage);
        packet.WriteString(_page.RequiredPermission);
        packet.WriteInteger(_page.OrderNum);
        packet.WriteBoolean(_page.Visible);
        packet.WriteBoolean(_page.Enabled);
        packet.WriteBoolean(_page.ClubOnly);
        packet.WriteString(_page.PageHeadline);
        packet.WriteString(_page.PageTeaser);
        packet.WriteString(_page.PageSpecial);
        packet.WriteString(_page.PageText1);
        packet.WriteString(_page.PageText2);
        packet.WriteString(_page.PageTextDetails);
        packet.WriteString(_page.PageTextTeaser);
        packet.WriteInteger(_page.RoomId);
        packet.WriteString(_page.Includes);
    }
}
