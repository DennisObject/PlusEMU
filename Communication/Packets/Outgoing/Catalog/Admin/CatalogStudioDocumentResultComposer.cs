using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// Octane-Renderer's CatalogStudioDocumentResultMessageParser (export, dry run and apply), here only as a refusal:
// an empty legacy document, no fingerprint and nothing changed.
public sealed class CatalogStudioDocumentResultComposer : IServerPacket
{
    private readonly string _operationId;
    private readonly string _code;
    private readonly string _message;
    private readonly int _revision;
    private readonly string _format;

    public uint MessageId => ServerPacketHeader.CatalogStudioDocumentResultComposer;

    public CatalogStudioDocumentResultComposer(string operationId, string code, string message, int revision, string format)
    {
        _operationId = operationId;
        _code = code;
        _message = message;
        _revision = revision;
        _format = format;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_operationId);
        packet.WriteBoolean(false);
        packet.WriteString(_code);
        packet.WriteString(_message);
        packet.WriteInteger(_revision);
        packet.WriteString(_format);
        packet.WriteString(string.Empty);
        packet.WriteString(string.Empty);
        packet.WriteInteger(0);
    }
}
