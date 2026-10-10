using System.Text.Json.Serialization;

namespace Plus.Communication.Revisions;

public class Revision
{
    // Packet headers are supplied by the current registered protocol; partial fixtures must not fabricate them.
    private IReadOnlyDictionary<string, uint>? _incomingHeaders;
    private IReadOnlyDictionary<string, uint>? _outgoingHeaders;

    public string Name { get; set; } = string.Empty;
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

    private InvalidOperationException MissingHeaders(string direction) => new($"{Name}: missing {direction} headers.");
}
