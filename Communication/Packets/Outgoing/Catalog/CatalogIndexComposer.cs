using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogIndexComposer : IServerPacket
{
    // The client rejects deeper trees, so pages below this depth are left out.
    public const int MaximumDepth = 20;

    private readonly GameClient _session;
    private readonly string _mode;
    private readonly ILookup<int, CatalogPage> _children;

    public uint MessageId => ServerPacketHeader.CatalogIndexComposer;

    public CatalogIndexComposer(GameClient session, ICollection<CatalogPage> pages, string mode = CatalogModes.Normal)
    {
        _session = session;
        _mode = mode;
        var habbo = session.GetHabbo();
        _children = pages.Where(page => page.CatalogMode == mode && page.IsAvailableTo(habbo)).ToLookup(page => page.ParentId);
    }

    public void Compose(IOutgoingPacket packet)
    {
        WriteRootIndex(packet);
        foreach (var page in _children[-1])
            WriteNode(packet, page, 1);
        packet.WriteBoolean(false);
        packet.WriteString(_mode);
    }

    public void WriteRootIndex(IOutgoingPacket packet)
    {
        packet.WriteBoolean(true);
        packet.WriteInteger(0);
        packet.WriteInteger(-1);
        packet.WriteInteger(-1);
        packet.WriteString("root");
        packet.WriteString(string.Empty);
        packet.WriteInteger(0);
        packet.WriteInteger(_children[-1].Count());
    }

    private void WriteNode(IOutgoingPacket packet, CatalogPage page, int depth)
    {
        var children = depth < MaximumDepth ? _children[page.Id].ToList() : new List<CatalogPage>();
        packet.WriteBoolean(page.Visible);
        packet.WriteInteger(page.Icon);
        // A disabled page stays in the tree as a heading the client cannot open.
        packet.WriteInteger(page.Enabled ? page.Id : -1);
        packet.WriteInteger(page.ParentId);
        packet.WriteString(page.Link);
        packet.WriteString(page.Caption);
        var offerIds = page.Enabled ? CatalogOfferIndex.OfficialOfferIds(page).ToList() : new List<int>();
        packet.WriteInteger(offerIds.Count);
        foreach (var offerId in offerIds) packet.WriteInteger(offerId);
        packet.WriteInteger(children.Count);
        foreach (var child in children)
            WriteNode(packet, child, depth + 1);
    }
}
