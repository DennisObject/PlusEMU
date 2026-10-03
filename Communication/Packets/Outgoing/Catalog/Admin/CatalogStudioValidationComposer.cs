using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// Octane-Renderer's CatalogStudioValidationMessageParser. PlusEMU validates each edit as it is saved and has no
// separate validation pass, so this always answers without issues and with the reason in code and message.
public sealed class CatalogStudioValidationComposer : IServerPacket
{
    private readonly string _operationId;
    private readonly string _code;
    private readonly string _message;
    private readonly int _revision;

    public uint MessageId => ServerPacketHeader.CatalogStudioValidationComposer;

    public CatalogStudioValidationComposer(string operationId, string code, string message, int revision)
    {
        _operationId = operationId;
        _code = code;
        _message = message;
        _revision = revision;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_operationId);
        packet.WriteBoolean(false);
        packet.WriteString(_code);
        packet.WriteString(_message);
        packet.WriteInteger(_revision);
        packet.WriteBoolean(true);
        packet.WriteInteger(0);
    }
}
