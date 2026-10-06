using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog.Admin;

// success, message, then for the editor's smart saves (createPage/savePage/createOffer/saveOffer) the acknowledgement
// Octane-Renderer's CatalogAdminResultMessageParser reads: protocol 1, operationId, action, code, draftVersionId,
// revision, entityType, catalogType, entityId, entity JSON, history group JSON, field errors JSON, server duration.
public sealed class CatalogAdminResultComposer : IServerPacket
{
    public const int SmartSaveProtocol = 1;
    public const string HistorySource = "CATALOG_ADMIN";
    private const int MaxWireStringBytes = ushort.MaxValue;

    internal static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly bool _success;
    private readonly string _message;
    private readonly CatalogAdminSmartSave? _smartSave;

    public uint MessageId => ServerPacketHeader.CatalogAdminResultComposer;

    public CatalogAdminResultComposer(bool success, string message, CatalogAdminSmartSave? smartSave = null)
    {
        _success = success;
        _message = message;
        _smartSave = smartSave;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_success);
        packet.WriteString(_message);

        if (_smartSave is not { } save)
        {
            return;
        }

        var outcome = save.Outcome;
        packet.WriteInteger(SmartSaveProtocol);
        packet.WriteString(save.OperationId);
        packet.WriteString(save.Action);
        packet.WriteString(outcome.Code);
        packet.WriteInteger(CatalogAdminEnvelope.LiveVersionId);
        packet.WriteInteger(outcome.Revision);
        packet.WriteString(outcome.EntityType);
        packet.WriteString(outcome.CatalogType);
        packet.WriteInteger(outcome.EntityId);
        packet.WriteString(Fit(outcome.Entity == null ? string.Empty : JsonSerializer.Serialize(outcome.Entity, outcome.Entity.GetType(), WireJson)));
        packet.WriteString(outcome.History == null ? "null" : Fit(HistoryGroup(outcome.History, save.ActorName)));
        packet.WriteString(Fit(JsonSerializer.Serialize(outcome.FieldErrors, WireJson), "{}"));
        packet.WriteInteger(0);
    }

    internal static string HistoryGroup(CatalogAdminLogEntry entry, string actorName) => JsonSerializer.Serialize(new
    {
        id = entry.Id,
        revision = entry.Id,
        actorId = entry.UserId,
        actorName,
        summary = entry.Summary,
        source = HistorySource,
        createdAt = entry.CreatedAt.ToString("s"),
        entries = new[] { new { entityType = entry.EntityType, catalogType = entry.CatalogType, entityId = entry.EntityId, operation = entry.Operation } }
    }, WireJson);

    // A string longer than the wire allows would corrupt the packet; the editor treats an empty entity as an incomplete save.
    private static string Fit(string json, string fallback = "") => Encoding.UTF8.GetByteCount(json) <= MaxWireStringBytes ? json : fallback;
}

public sealed record CatalogAdminSmartSave(string OperationId, string Action, CatalogAdminOutcome Outcome, string ActorName);
