using System.Reflection;
using System.Text.Json;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.Communication.Packets.Incoming.Handshake;
using Plus.HabboHotel.GameClients;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Plus.Tests;

[Collection("Revision cache startup")]
public class RevisionMappingTests
{
    [Fact]
    public async Task CacheStartupPreservesCurrentInternalIdsAndLoadsBothLegacyProfiles()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var revisionDirectory = Path.Combine(temporaryDirectory, "revisions");
        Directory.CreateDirectory(revisionDirectory);

        foreach (var file in new[] { "1.6.6.json", "3.6.0.json" }) {
            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Resources", "Revisions", file));
            File.Copy(source, Path.Combine(revisionDirectory, file));
        }

        try {
            var cache = new RevisionsCache();
            typeof(RevisionsCache).GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(cache, revisionDirectory);
            await cache.Start();

            Assert.Equal(3, cache.Revisions.Count);
            Assert.Equal(ClientPacketHeader.GetBundleDiscountRulesetEvent,
                cache.InternalRevision.IncomingIdToInternalIdMapping[ClientPacketHeader.GetBundleDiscountRulesetEvent]);
            Assert.All(cache.InternalRevision.IncomingIdToInternalIdMapping, entry => Assert.Equal(entry.Key, entry.Value));
            Assert.All(cache.InternalRevision.InternalIdToOutgoingIdMapping, entry => Assert.Equal(entry.Key, entry.Value));
            Assert.Equal(ClientPacketHeader.SSOTicketEvent, cache.Revisions["NITRO-3-6-0"].IncomingIdToInternalIdMapping[2419]);
            Assert.Equal(2491u, cache.Revisions["NITRO-3-6-0"].InternalIdToOutgoingIdMapping[ServerPacketHeader.AuthenticationOkComposer]);
        }
        finally {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Theory]
    [InlineData("1.6.6.json")]
    [InlineData("3.6.0.json")]
    public void ExistingProfilesKeepTheirExactTranslations(string file)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Resources", "Revisions", file));
        var revision = JsonSerializer.Deserialize<Revision>(File.ReadAllText(path))!;
        var internalRevision = new Revision
        {
            ZeroHeaderIsValid = true,
            IncomingHeaders = Headers(typeof(ClientPacketHeader)),
            OutgoingHeaders = Headers(typeof(ServerPacketHeader))
        };

        revision.BuildMappings(internalRevision);

        foreach (var (key, wireId) in revision.IncomingHeaders.Where(entry => entry.Value > 0)) {
            Assert.Equal(internalRevision.IncomingHeaders[key], revision.IncomingIdToInternalIdMapping[wireId]);
        }

        foreach (var (key, wireId) in revision.OutgoingHeaders.Where(entry => entry.Value > 0)) {
            Assert.Equal(wireId, revision.InternalIdToOutgoingIdMapping[internalRevision.OutgoingHeaders[key]]);
        }

        Assert.Equal(revision.IncomingHeaders.Count(entry => entry.Value > 0), revision.IncomingIdToInternalIdMapping.Count);
        Assert.Equal(revision.OutgoingHeaders.Count(entry => entry.Value > 0), revision.InternalIdToOutgoingIdMapping.Count);
        Assert.Equal(4000u, revision.IncomingIdToInternalIdMapping[4000]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidRoutesFailBeforeEitherMappingIsPublished(bool incoming)
    {
        var internalRevision = Profile(new() { ["First"] = 10, ["Second"] = 20, ["Disabled"] = 0 });
        var invalid = new (Dictionary<string, uint> Headers, string Error)[]
        {
            (new() { ["Unknown"] = 1 }, "unknown"),
            (new() { ["First"] = 65536 }, "16-bit"),
            (new() { ["Disabled"] = 1 }, "no internal ID"),
            (new() { ["First"] = 1, ["Second"] = 1 }, "duplicate")
        };

        foreach (var (headers, error) in invalid) {
            var revision = Profile(new() { ["First"] = 71 });

            if (incoming) {
                revision.IncomingHeaders = headers;
            }
            else {
                revision.OutgoingHeaders = headers;
            }

            var exception = Assert.Throws<InvalidOperationException>(() => revision.BuildMappings(internalRevision));
            Assert.Contains(error, exception.Message);
            Assert.Contains(incoming ? "incoming" : "outgoing", exception.Message);
            Assert.Null(revision.IncomingIdToInternalIdMapping);
            Assert.Null(revision.InternalIdToOutgoingIdMapping);
        }
    }

    [Fact]
    public void MissingNameOrDirectionCannotPublishAPartialMapping()
    {
        var internalRevision = Profile(new() { ["First"] = 10 });

        foreach (var failure in new[] { "name", "incoming", "outgoing" }) {
            var revision = Profile(new() { ["First"] = 71 });

            if (failure == "name") {
                revision.Name = " ";
            }
            else if (failure == "incoming") {
                revision.IncomingHeaders = null!;
            }
            else {
                revision.OutgoingHeaders = null!;
            }

            Assert.Contains(failure, Assert.Throws<InvalidOperationException>(() => revision.BuildMappings(internalRevision)).Message);
            Assert.Null(revision.IncomingIdToInternalIdMapping);
            Assert.Null(revision.InternalIdToOutgoingIdMapping);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InternalAliasesCannotMakeTheWireMapAmbiguous(bool incoming)
    {
        var internalRevision = Profile(new() { ["First"] = 10, ["Alias"] = 10 });
        var revision = Profile(new() { ["First"] = 71 });

        if (incoming) {
            revision.IncomingHeaders = new Dictionary<string, uint> { ["First"] = 71, ["Alias"] = 72 };
        }
        else {
            revision.OutgoingHeaders = new Dictionary<string, uint> { ["First"] = 71, ["Alias"] = 72 };
        }

        Assert.Contains("duplicate", Assert.Throws<InvalidOperationException>(() => revision.BuildMappings(internalRevision)).Message);
    }

    [Fact]
    public void DirectionsMayReuseWireIdsAndZeroExplicitlyDisablesARoute()
    {
        var internalRevision = Profile(new() { ["First"] = 10, ["Disabled"] = 0 });
        var revision = Profile(new() { ["First"] = 65535, ["Disabled"] = 0 });
        revision.BuildMappings(internalRevision);

        Assert.Equal(10u, revision.IncomingIdToInternalIdMapping[65535]);
        Assert.Equal(65535u, revision.InternalIdToOutgoingIdMapping[10]);
        Assert.Single(revision.IncomingIdToInternalIdMapping);
        Assert.Single(revision.InternalIdToOutgoingIdMapping);
        Assert.False(revision.IncomingIdToInternalIdMapping.ContainsKey(0));
    }

    [Fact]
    public void ExplicitZeroWireHeaderTranslatesAndSerializesWithoutChangingLegacyDisabledEntries()
    {
        var internalRevision = Profile(new() { ["InfoRetrieve"] = 80001 });
        var revision = Profile(new() { ["InfoRetrieve"] = 0 });
        revision.BuildMappings(internalRevision);
        Assert.Empty(revision.IncomingIdToInternalIdMapping);
        Assert.Empty(revision.InternalIdToOutgoingIdMapping);

        revision.ZeroHeaderIsValid = true;
        revision.BuildMappings(internalRevision);
        Assert.Equal(80001u, revision.IncomingIdToInternalIdMapping[0]);
        Assert.Equal(0u, revision.InternalIdToOutgoingIdMapping[80001]);

        var client = new FlashGameClient(null!, null!, TestLogging.GameClient);
        var frame = new byte[6];
        client.CreateHeader(frame, revision.InternalIdToOutgoingIdMapping[80001]);
        Assert.Equal(new byte[] { 0, 0, 0, 2, 0, 0 }, frame);
        var decoded = client.GetMessageIdAndPacketLength(frame);
        Assert.True(decoded.Complete);
        Assert.False(decoded.Malformed);
        Assert.Equal(0u, decoded.MessageId);
        Assert.Equal(80001u, revision.IncomingIdToInternalIdMapping[decoded.MessageId]);
    }

    [Fact]
    public void ZeroWirePolicyDoesNotPermitMissingInternalIdsOrDuplicateZeroRoutes()
    {
        var internalRevision = Profile(new() { ["First"] = 10, ["Second"] = 20, ["Disabled"] = 0 });
        var revision = Profile(new() { ["First"] = 0, ["Second"] = 0 });
        revision.ZeroHeaderIsValid = true;
        Assert.Contains("duplicate", Assert.Throws<InvalidOperationException>(() => revision.BuildMappings(internalRevision)).Message);

        revision.IncomingHeaders = new Dictionary<string, uint> { ["Disabled"] = 0 };
        Assert.Contains("no internal ID", Assert.Throws<InvalidOperationException>(() => revision.BuildMappings(internalRevision)).Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConcatenatedHelloSelectsRevisionBeforeTranslatingZeroHeader(bool knownBuild)
    {
        var internalRevision = new Revision
        {
            ZeroHeaderIsValid = true,
            IncomingHeaders = Headers(typeof(ClientPacketHeader)),
            OutgoingHeaders = Headers(typeof(ServerPacketHeader)),
            IncomingIdToInternalIdMapping = new Dictionary<uint, uint>
            {
                [ClientPacketHeader.ClientHelloEvent] = ClientPacketHeader.ClientHelloEvent
            }
        };
        var selected = new Revision
        {
            Name = "test-air-header-profile",
            ZeroHeaderIsValid = true,
            IncomingHeaders = new Dictionary<string, uint>
            {
                [nameof(ClientPacketHeader.ClientHelloEvent)] = 4000,
                [nameof(ClientPacketHeader.InfoRetrieveEvent)] = 0
            },
            OutgoingHeaders = new Dictionary<string, uint>()
        };
        selected.BuildMappings(internalRevision);
        var cache = new HandshakeCache(internalRevision, selected);
        var server = new HandshakeServer(new ClientHelloEvent(cache, NullLogger<ClientHelloEvent>.Instance));
        var disconnected = 0;
        var client = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = internalRevision,
            DisconnectRequested = () => disconnected++
        };

        using var stream = PlusMemoryStream.GetStream();
        var hello = new FlashOutgoingPacket(stream);
        hello.WriteString(knownBuild ? selected.Name : "unknown-build");
        hello.WriteString("HTML5");
        hello.WriteInt(0);
        hello.WriteInt(0);
        var helloFrame = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        client.CreateHeader(helloFrame, 4000);
        var concatenated = helloFrame.ToArray().Concat(new byte[] { 0, 0, 0, 2, 0, 0 }).ToArray();

        client.OnReceived(concatenated, 0, concatenated.Length);

        Assert.Equal(knownBuild ? 0 : 1, disconnected);
        Assert.Same(knownBuild ? selected : internalRevision, client.Revision);
        Assert.Equal(knownBuild
            ? new[] { ClientPacketHeader.ClientHelloEvent, ClientPacketHeader.InfoRetrieveEvent }
            : new[] { ClientPacketHeader.ClientHelloEvent }, server.MessageIds);
        Assert.Null(client._incompleteStream);
    }

    [Theory]
    [InlineData("null", "empty packet revision")]
    [InlineData("{}", "name is missing")]
    [InlineData("{\"Name\":\" \"}", "name is missing")]
    public async Task CacheRejectsNullOrUnnamedFilesWithTheirSourcePath(string json, string error)
    {
        var directory = Directory.CreateTempSubdirectory("invalid-revision-").FullName;

        try {
            var path = Path.Combine(directory, "invalid.json");
            await File.WriteAllTextAsync(path, json);
            var cache = CacheAt(directory);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => cache.Start());
            Assert.Contains(path, exception.Message);
            Assert.Contains(error, exception.Message);
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CacheRejectsDuplicateNamesInsteadOfChoosingTheLastFile()
    {
        var directory = Directory.CreateTempSubdirectory("duplicate-revision-").FullName;

        try {
            const string json = "{\"Name\":\"Duplicate\",\"IncomingHeaders\":{},\"OutgoingHeaders\":{}}";
            await File.WriteAllTextAsync(Path.Combine(directory, "first.json"), json);
            await File.WriteAllTextAsync(Path.Combine(directory, "second.json"), json);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CacheAt(directory).Start());
            Assert.Contains("duplicate packet revision name 'Duplicate'", exception.Message);
            Assert.Contains(".json", exception.Message);
        }
        finally {
            Directory.Delete(directory, true);
        }
    }

    private static RevisionsCache CacheAt(string directory)
    {
        var cache = new RevisionsCache();
        typeof(RevisionsCache).GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cache, directory);

        return cache;
    }

    private sealed class HandshakeCache(Revision internalRevision, Revision selected) : IRevisionsCache
    {
        public IReadOnlyDictionary<string, Revision> Revisions { get; set; } =
            new Dictionary<string, Revision> { [selected.Name] = selected };
        public Revision InternalRevision { get; } = internalRevision;
    }

    private sealed class HandshakeServer(ClientHelloEvent hello) : IGameServer
    {
        public List<uint> MessageIds { get; } = new();
        public bool Start() => true;
        public bool Stop() => true;
        public bool ModifyOutgoingPacket(GameClient client, IOutgoingPacket packet) => true;
        public bool HasOutgoingPacketInjectors(uint messageId) => false;
        public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet)
        {
            MessageIds.Add(messageId);

            return messageId == ClientPacketHeader.ClientHelloEvent
                ? hello.Parse(client, packet)
                : Task.CompletedTask;
        }
    }

    private static Revision Profile(Dictionary<string, uint> headers) => new()
    {
        Name = "test-profile",
        IncomingHeaders = headers,
        OutgoingHeaders = headers
    };

    private static Dictionary<string, uint> Headers(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue()!);
}

[CollectionDefinition("Revision cache startup", DisableParallelization = true)]
public class RevisionCacheStartupCollection;
