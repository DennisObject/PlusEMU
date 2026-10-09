using System.Text.Json.Serialization;

namespace Plus.Communication.Revisions;

public class Revision
{
    public string Name { get; set; }
    public bool ZeroHeaderIsValid { get; set; }
    public IReadOnlyDictionary<string, uint> IncomingHeaders { get; set; }
    [JsonIgnore]
    public IReadOnlyDictionary<uint, uint> IncomingIdToInternalIdMapping { get; set; }
    public IReadOnlyDictionary<string, uint> OutgoingHeaders { get; set; }
    [JsonIgnore]
    public IReadOnlyDictionary<uint, uint> InternalIdToOutgoingIdMapping { get; set; }

    public void BuildMappings(Revision internalRevision)
    {
        if (string.IsNullOrWhiteSpace(Name)) {
            throw new InvalidOperationException("A packet revision must have a name.");
        }

        var incoming = BuildMapping(IncomingHeaders, internalRevision.IncomingHeaders, "incoming", true);
        var outgoing = BuildMapping(OutgoingHeaders, internalRevision.OutgoingHeaders, "outgoing", false);
        IncomingIdToInternalIdMapping = incoming;
        InternalIdToOutgoingIdMapping = outgoing;
    }

    private Dictionary<uint, uint> BuildMapping(IReadOnlyDictionary<string, uint> headers,
        IReadOnlyDictionary<string, uint> internalHeaders, string direction, bool incoming)
    {
        if (headers == null) {
            throw new InvalidOperationException($"{Name}: missing {direction} headers.");
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

            if (internalId == 0) {
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
