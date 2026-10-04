using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// Octane-Renderer's CatalogStudioHistoryMessageParser: one group per audit row, each with its one entry.
public sealed class CatalogStudioHistoryComposer : IServerPacket
{
    private readonly CatalogAdminHistory _history;

    public uint MessageId => ServerPacketHeader.CatalogStudioHistoryComposer;

    public CatalogStudioHistoryComposer(CatalogAdminHistory history) => _history = history;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(CatalogAdminEnvelope.LiveVersionId);
        packet.WriteInteger(_history.Revision);
        packet.WriteInteger(_history.TotalCount);
        packet.WriteInteger(_history.Groups.Count);
        foreach (var entry in _history.Groups)
        {
            packet.WriteInteger(entry.Id);
            packet.WriteInteger(entry.Id);
            packet.WriteInteger(entry.UserId);
            packet.WriteString(entry.Username);
            packet.WriteString(entry.Summary);
            packet.WriteString(CatalogAdminResultComposer.HistorySource);
            packet.WriteString(entry.CreatedAt.ToString("s"));
            packet.WriteInteger(1);
            packet.WriteString(entry.EntityType);
            packet.WriteInteger(entry.EntityId);
            packet.WriteString(entry.Operation);
        }
    }
}
