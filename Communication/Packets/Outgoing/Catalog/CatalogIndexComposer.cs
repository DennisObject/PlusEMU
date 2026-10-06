using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogIndexComposer : IServerPacket
{
    private readonly CatalogIndexSnapshot _index;

    public uint MessageId => ServerPacketHeader.CatalogIndexComposer;

    public CatalogIndexComposer(CatalogIndexSnapshot index)
    {
        _index = index;
    }

    public void Compose(IOutgoingPacket packet)
    {
        WriteRootIndex(packet);

        foreach (var node in _index.Roots)
        {
            WriteNode(packet, node);
        }

        packet.WriteBoolean(false);
        packet.WriteString(_index.Mode);
    }

    private void WriteRootIndex(IOutgoingPacket packet)
    {
        packet.WriteBoolean(true);
        packet.WriteInteger(0);
        packet.WriteInteger(-1);
        packet.WriteInteger(-1);
        packet.WriteString("root");
        packet.WriteString(string.Empty);
        packet.WriteInteger(0);
        packet.WriteInteger(_index.Roots.Length);
    }

    private static void WriteNode(IOutgoingPacket packet, CatalogIndexNode node)
    {
        packet.WriteBoolean(node.Visible);
        packet.WriteInteger(node.Icon);
        packet.WriteInteger(node.WireId);
        packet.WriteInteger(node.ParentId);
        packet.WriteString(node.Link);
        packet.WriteString(node.Caption);
        packet.WriteInteger(node.OfferIds.Length);

        foreach (var offerId in node.OfferIds)
        {
            packet.WriteInteger(offerId);
        }

        packet.WriteInteger(node.Children.Length);

        foreach (var child in node.Children)
        {
            WriteNode(packet, child);
        }
    }
}
