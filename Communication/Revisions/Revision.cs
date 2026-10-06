using System.Text.Json.Serialization;

namespace Plus.Communication.Revisions;

public class Revision
{
    public string Name
    {
        get; set;
    }
    public IReadOnlyDictionary<string, uint> IncomingHeaders
    {
        get; set;
    }
    [JsonIgnore]
    public IReadOnlyDictionary<uint, uint> IncomingIdToInternalIdMapping
    {
        get; set;
    }
    public IReadOnlyDictionary<string, uint> OutgoingHeaders
    {
        get; set;
    }
    [JsonIgnore]
    public IReadOnlyDictionary<uint, uint> InternalIdToOutgoingIdMapping
    {
        get; set;
    }
}
