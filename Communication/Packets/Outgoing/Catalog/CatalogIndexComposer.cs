using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogIndexComposer : IServerPacket
{
    private readonly GameClient _session;
    private readonly ICollection<CatalogPage> _pages;

    public uint MessageId => ServerPacketHeader.CatalogIndexComposer;

    public CatalogIndexComposer(GameClient session, ICollection<CatalogPage> pages)
    {
        _session = session;
        _pages = pages;
    }

    public void Compose(IOutgoingPacket packet)
    {
        WriteRootIndex(packet);
        foreach (var parent in _pages)
        {
            if (parent.ParentId != -1 || parent.MinimumRank > _session.GetHabbo().Rank || parent.MinimumVip > _session.GetHabbo().VipRank && _session.GetHabbo().Rank == 1)
                continue;
            WritePage(packet, parent, CalcTreeSize(_pages, parent.Id));
            foreach (var child in _pages)
            {
                if (child.ParentId != parent.Id || child.MinimumRank > _session.GetHabbo().Rank || child.MinimumVip > _session.GetHabbo().VipRank && _session.GetHabbo().Rank == 1)
                    continue;
                if (child.Enabled)
                    WritePage(packet, child, CalcTreeSize(_pages, child.Id));
                else
                    WriteNodeIndex(packet, child, CalcTreeSize(_pages, child.Id));
                foreach (var subChild in _pages)
                {
                    if (subChild.ParentId != child.Id || subChild.MinimumRank > _session.GetHabbo().Rank)
                        continue;
                    WritePage(packet, subChild, 0);
                }
            }
        }
        packet.WriteBoolean(false);
        packet.WriteString("NORMAL");
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
        packet.WriteInteger(CalcTreeSize(_pages, -1));
    }

    public void WriteNodeIndex(IOutgoingPacket packet, CatalogPage page, int treeSize)
    {
        packet.WriteBoolean(page.Visible);
        packet.WriteInteger(page.Icon);
        packet.WriteInteger(-1);
        packet.WriteInteger(page.ParentId);
        packet.WriteString(page.Link);
        packet.WriteString(page.Caption);
        packet.WriteInteger(0);
        packet.WriteInteger(treeSize);
    }

    public void WritePage(IOutgoingPacket packet, CatalogPage page, int treeSize)
    {
        packet.WriteBoolean(page.Visible);
        packet.WriteInteger(page.Icon);
        packet.WriteInteger(page.Id);
        packet.WriteInteger(page.ParentId);
        packet.WriteString(page.Link);
        packet.WriteString(page.Caption);
        packet.WriteInteger(page.ItemOffers.Count);
        foreach (var i in page.ItemOffers.Keys) packet.WriteInteger(i);
        packet.WriteInteger(treeSize);
    }

    public int CalcTreeSize(ICollection<CatalogPage> pages, int parentId)
    {
        var i = 0;
        foreach (var page in pages)
        {
            if (page.MinimumRank > _session.GetHabbo().Rank || page.MinimumVip > _session.GetHabbo().VipRank && _session.GetHabbo().Rank == 1 || page.ParentId != parentId)
                continue;
            if (page.ParentId == parentId)
                i++;
        }
        return i;
    }
}