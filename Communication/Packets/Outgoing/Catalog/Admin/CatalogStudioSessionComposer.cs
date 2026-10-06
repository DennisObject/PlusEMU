using System.IO.Compression;
using System.Text.Json;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// Octane-Renderer's CatalogStudioSessionMessageParser. PlusEMU edits the live catalog: the active and draft version
// are the same, nothing is pending and there are no published versions. Pages travel as gzip+base64 JSON chunks.
public sealed class CatalogStudioSessionComposer : IServerPacket
{
    public const string SnapshotEncoding = "GZIP_BASE64_JSON";
    public const int ChunkLength = 60_000;

    private readonly CatalogAdminSession _session;

    public uint MessageId => ServerPacketHeader.CatalogStudioSessionComposer;

    public CatalogStudioSessionComposer(CatalogAdminSession session) => _session = session;

    public void Compose(IOutgoingPacket packet)
    {
        var updatedAt = _session.UpdatedAt?.ToString("s") ?? string.Empty;
        packet.WriteInteger(CatalogAdminEnvelope.LiveVersionId);
        packet.WriteInteger(CatalogAdminEnvelope.LiveVersionId);
        packet.WriteInteger(_session.Revision);
        packet.WriteString(updatedAt);
        packet.WriteString(updatedAt);
        packet.WriteInteger(0); // pending changes
        packet.WriteInteger(0); // actors
        packet.WriteBoolean(true); // validation current
        packet.WriteInteger(0); // validation issues
        packet.WriteInteger(0); // published versions
        packet.WriteString(SnapshotEncoding);
        var chunks = Chunks(_session.Pages);
        packet.WriteInteger(chunks.Count);

        foreach (var chunk in chunks)
        {
            packet.WriteString(chunk);
        }
    }

    internal static List<string> Chunks(IReadOnlyList<CatalogAdminPage> pages)
    {
        using var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, pages, CatalogAdminResultComposer.WireJson);
        }

        var encoded = Convert.ToBase64String(buffer.ToArray());
        var chunks = new List<string>();

        for (int start = 0; start < encoded.Length; start += ChunkLength)
        {
            chunks.Add(encoded.Substring(start, Math.Min(ChunkLength, encoded.Length - start)));
        }

        return chunks;
    }
}
