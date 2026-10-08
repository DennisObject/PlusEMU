using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Outgoing.FurniEditor;

// Octane-Renderer's FurniEditorDetailResultMessageParser: FurniDetailData, catalog references, furnidata entry, diagnostic.
public sealed class FurniEditorDetailResultComposer : IServerPacket
{
    private readonly FurniEditorDetail _detail;

    public uint MessageId => ServerPacketHeader.FurniEditorDetailResultComposer;

    public FurniEditorDetailResultComposer(FurniEditorDetail detail) => _detail = detail;

    public void Compose(IOutgoingPacket packet)
    {
        var item = _detail.Item;
        FurniEditorItemWriter.Write(packet, item);
        packet.WriteBoolean(item.AllowGift);
        packet.WriteBoolean(item.AllowTrade);
        packet.WriteBoolean(item.AllowRecycle);
        packet.WriteBoolean(item.AllowMarketplaceSell);
        packet.WriteBoolean(item.AllowInventoryStack);
        packet.WriteString(item.VendingIds);
        packet.WriteString(item.CustomParams);
        packet.WriteInteger(item.EffectId);
        packet.WriteInteger(item.EffectId);
        packet.WriteString(item.ClothingOnWalk);
        packet.WriteString(item.Multiheight);
        packet.WriteString(item.Description);
        packet.WriteInteger(_detail.UsageCount);
        packet.WriteInteger(_detail.CatalogRefs.Count);

        foreach (var reference in _detail.CatalogRefs) {
            packet.WriteInteger(reference.Id);
            packet.WriteString(reference.CatalogName);
            packet.WriteInteger(reference.CostCredits);
            packet.WriteInteger(reference.CostPoints);
            packet.WriteInteger(reference.PointsType);
            packet.WriteInteger(reference.PageId);
            packet.WriteString(reference.PageName);
        }

        packet.WriteString(_detail.Furnidata.EntryJson);
        packet.WriteString(_detail.Furnidata.DiagnosticJson);
    }
}
