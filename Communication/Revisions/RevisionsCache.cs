using System.Reflection;
using System.Text.Json;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Outgoing;
using Plus.Core;

namespace Plus.Communication.Revisions;

public class RevisionsCache : IRevisionsCache, IStartable
{
    public IReadOnlyDictionary<string, Revision> Revisions { get; set; } = new Dictionary<string, Revision>();
    public Revision InternalRevision { get; } = CreateInternalRevision();

    private string? _directory;
    public string Location => _directory ??= Path.Join(Directory.GetCurrentDirectory(), "revisions");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task Start()
    {
        WriteExampleRevision();
        await LoadRevisions();
        Validate();
    }

    private static Revision CreateInternalRevision()
    {
        var incomingHeaders = ReadHeaderIds(typeof(ClientPacketHeader));
        var outgoingHeaders = ReadHeaderIds(typeof(ServerPacketHeader));

        return new()
        {
            Name = "WIN63-202609161723-93809945",
            ZeroHeaderIsValid = true,
            IncomingHeaders = incomingHeaders,
            IncomingIdToInternalIdMapping = incomingHeaders.ToDictionary(kvp => kvp.Value, kvp => kvp.Value),
            OutgoingHeaders = outgoingHeaders,
            InternalIdToOutgoingIdMapping = outgoingHeaders.Where(kvp => kvp.Value > 0).ToDictionary(kvp => kvp.Value, kvp => kvp.Value)
        };
    }

    private static Dictionary<string, uint> ReadHeaderIds(Type headers) =>
        headers.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).ToDictionary(field => field.Name,
            field => field.GetRawConstantValue() is uint id ? id : throw new InvalidOperationException($"{headers.Name}.{field.Name} is not a packet ID constant."));

    private void WriteExampleRevision()
    {
        if (!Directory.Exists(Location)) {
            Directory.CreateDirectory(Location);
        }

        File.WriteAllText(Path.Join(Location, "example.json"), JsonSerializer.Serialize(InternalRevision, SerializerOptions));
    }

    private async Task LoadRevisions()
    {
        var revisions = new Dictionary<string, Revision>();

        foreach (var file in Directory.GetFiles(Location).Where(f => f.EndsWith(".json"))) {
            var revision = JsonSerializer.Deserialize<Revision>(await File.ReadAllTextAsync(file), SerializerOptions)
                ?? throw new InvalidOperationException($"{file}: empty packet revision.");

            if (string.IsNullOrWhiteSpace(revision.Name)) {
                throw new InvalidOperationException($"{file}: packet revision name is missing.");
            }

            if (revision.Name.Equals(InternalRevision.Name)) {
                continue;
            }

            if (!revisions.TryAdd(revision.Name, revision)) {
                throw new InvalidOperationException($"{file}: duplicate packet revision name '{revision.Name}'.");
            }
        }

        revisions[InternalRevision.Name] = InternalRevision;
        Revisions = revisions;
    }

    private void Validate()
    {
        foreach (var revision in Revisions.Values) {
            if (ReferenceEquals(revision, InternalRevision)) {
                continue;
            }

            revision.BuildMappings(InternalRevision);
        }
    }
}
