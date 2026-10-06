using System.Reflection;
using System.Text.Json;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Outgoing;
using Plus.Core;

namespace Plus.Communication.Revisions;

public class RevisionsCache : IRevisionsCache, IStartable
{
    public IReadOnlyDictionary<string, Revision> Revisions { get; set; }
    public Revision InternalRevision { get; private set; }

    private string? _directory;
    public string Location => _directory ??= Path.Join(Directory.GetCurrentDirectory(), "revisions");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task Start()
    {
        LoadInternalRevision();
        await LoadRevisions();
        Validate();
    }

    private void LoadInternalRevision()
    {
        var incomingHeaders = typeof(ClientPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue());
        var outgoingHeaders = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue());
        InternalRevision = new()
        {
            Name = "PRODUCTION-201701242205-837386173",
            IncomingHeaders = incomingHeaders,
            IncomingIdToInternalIdMapping = incomingHeaders.Where(kvp => kvp.Value > 0).ToDictionary(kvp => kvp.Value, kvp => kvp.Value),
            OutgoingHeaders = outgoingHeaders,
            InternalIdToOutgoingIdMapping = outgoingHeaders.Where(kvp => kvp.Value > 0).ToDictionary(kvp => kvp.Value, kvp => kvp.Value)
        };

        if (!Directory.Exists(Location)) {
            Directory.CreateDirectory(Location);
        }

        File.WriteAllText(Path.Join(Location, "example.json"), JsonSerializer.Serialize(InternalRevision, SerializerOptions));
    }

    private async Task LoadRevisions()
    {
        var revisions = new Dictionary<string, Revision>();

        foreach (var file in Directory.GetFiles(Location).Where(f => f.EndsWith(".json"))) {
            var revision = JsonSerializer.Deserialize<Revision>(await File.ReadAllTextAsync(file), SerializerOptions);

            if (revision.Name.Equals(InternalRevision.Name)) {
                continue;
            }

            revisions[revision.Name] = revision;
        }

        revisions[InternalRevision.Name] = InternalRevision;
        Revisions = revisions;
    }

    private void Validate()
    {
        foreach (var revision in Revisions.Values) {
            var undefinedIncoming = revision.IncomingHeaders.Keys.Where(key => !InternalRevision.IncomingHeaders.ContainsKey(key)).ToList();
            var undefinedOutgoing = revision.OutgoingHeaders.Keys.Where(key => !InternalRevision.OutgoingHeaders.ContainsKey(key)).ToList();

            if (undefinedIncoming.Any()) {
                Console.WriteLine($"{revision.Name}: Missing Incoming Headers ({undefinedIncoming.Count}):");

                foreach (var incoming in undefinedIncoming) {
                    Console.WriteLine(incoming);
                }
            }

            if (undefinedOutgoing.Any()) {
                Console.WriteLine($"{revision.Name}: Missing Outgoing Headers ({undefinedOutgoing.Count}):");

                foreach (var outgoing in undefinedOutgoing) {
                    Console.WriteLine(outgoing);
                }
            }


            revision.IncomingIdToInternalIdMapping = revision.IncomingHeaders.Where(kvp => kvp.Value > 0).ToDictionary(kvp => kvp.Value, kvp => InternalRevision.IncomingHeaders[kvp.Key]);
            revision.InternalIdToOutgoingIdMapping = revision.OutgoingHeaders.Where(kvp => kvp.Value > 0).ToDictionary(kvp => InternalRevision.OutgoingHeaders[kvp.Key], kvp => kvp.Value);
        }
    }
}
