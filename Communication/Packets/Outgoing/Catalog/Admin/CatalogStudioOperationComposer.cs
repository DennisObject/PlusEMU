using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// Octane-Renderer's CatalogStudioOperationMessageParser (the answer to undo): operationId, success, code, message,
// revision and the entities that changed.
public sealed class CatalogStudioOperationComposer : IServerPacket
{
    private readonly string _operationId;
    private readonly bool _success;
    private readonly string _code;
    private readonly string _message;
    private readonly int _revision;
    private readonly IReadOnlyList<(string EntityType, int EntityId)> _changed;

    public uint MessageId => ServerPacketHeader.CatalogStudioOperationComposer;

    public CatalogStudioOperationComposer(string operationId, bool success, string code, string message, int revision, IReadOnlyList<(string EntityType, int EntityId)> changed)
    {
        _operationId = operationId;
        _success = success;
        _code = code;
        _message = message;
        _revision = revision;
        _changed = changed.ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_operationId);
        packet.WriteBoolean(_success);
        packet.WriteString(_code);
        packet.WriteString(_message);
        packet.WriteInteger(_revision);
        packet.WriteInteger(_changed.Count);

        foreach (var (entityType, entityId) in _changed) {
            packet.WriteString(entityType);
            packet.WriteInteger(entityId);
        }
    }
}
