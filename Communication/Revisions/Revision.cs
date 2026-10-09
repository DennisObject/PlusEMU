using System.Text.Json.Serialization;

namespace Plus.Communication.Revisions;

public class Revision
{
    // Profiles come from JSON, so a header table can be absent until BuildMappings rejects it.
    private IReadOnlyDictionary<string, uint>? _incomingHeaders;
    private IReadOnlyDictionary<string, uint>? _outgoingHeaders;

    public string Name { get; set; } = string.Empty;
    public bool ZeroHeaderIsValid { get; set; }
    public IReadOnlyDictionary<string, uint> IncomingHeaders
    {
        get => _incomingHeaders ?? throw MissingHeaders("incoming");
        set => _incomingHeaders = value;
    }
    [JsonIgnore]
    public IReadOnlyDictionary<uint, uint> IncomingIdToInternalIdMapping { get; set; } = new Dictionary<uint, uint>();
    public IReadOnlyDictionary<string, uint> OutgoingHeaders
    {
        get => _outgoingHeaders ?? throw MissingHeaders("outgoing");
        set => _outgoingHeaders = value;
    }
    [JsonIgnore]
    public IReadOnlyDictionary<uint, uint> InternalIdToOutgoingIdMapping { get; set; } = new Dictionary<uint, uint>();

    public void BuildMappings(Revision internalRevision)
    {
        if (string.IsNullOrWhiteSpace(Name)) {
            throw new InvalidOperationException("A packet revision must have a name.");
        }

        var incoming = BuildMapping(_incomingHeaders, internalRevision.IncomingHeaders, "incoming", true, internalRevision.ZeroHeaderIsValid);
        var outgoing = BuildMapping(_outgoingHeaders, internalRevision.OutgoingHeaders, "outgoing", false, false);
        IncomingIdToInternalIdMapping = incoming;
        InternalIdToOutgoingIdMapping = outgoing;
    }

    private InvalidOperationException MissingHeaders(string direction) => new($"{Name}: missing {direction} headers.");

    private Dictionary<uint, uint> BuildMapping(IReadOnlyDictionary<string, uint>? headers,
        IReadOnlyDictionary<string, uint> internalHeaders, string direction, bool incoming, bool zeroInternalIdIsValid)
    {
        if (headers == null) {
            throw MissingHeaders(direction);
        }

        var mapping = new Dictionary<uint, uint>();
        var wireIds = new HashSet<uint>();
        var internalIds = new HashSet<uint>();

        foreach (var (key, wireId) in headers) {
            if (!internalHeaders.TryGetValue(key, out var internalId)) {
                throw new InvalidOperationException($"{Name}: unknown {direction} packet key '{key}'.");
            }

            if (wireId > ushort.MaxValue) {
                throw new InvalidOperationException($"{Name}: {direction} packet '{key}' header {wireId} exceeds the 16-bit wire range.");
            }

            if (wireId == 0 && !ZeroHeaderIsValid) {
                continue;
            }

            if (internalId == 0 && !zeroInternalIdIsValid) {
                throw new InvalidOperationException($"{Name}: {direction} packet '{key}' has no internal ID.");
            }

            if (!wireIds.Add(wireId)) {
                throw new InvalidOperationException($"{Name}: duplicate {direction} wire header {wireId}.");
            }

            if (!internalIds.Add(internalId)) {
                throw new InvalidOperationException($"{Name}: duplicate {direction} internal ID {internalId}.");
            }

            mapping.Add(incoming ? wireId : internalId, incoming ? internalId : wireId);
        }

        return mapping;
    }
}
