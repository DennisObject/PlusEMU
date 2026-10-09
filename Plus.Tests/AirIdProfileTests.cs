using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

// These tests verify installed opt-in routing and opaque Flash payload preservation.
// They do not assert native AIR schemas or execute authentication/game handlers.
[Collection("Revision cache startup")]
public class AirIdProfileTests
{
    private const string ProfileName = "OCTANE-AIR-IDS-WIN63-202609161723-93809945";
    private static readonly string EmulatorRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void StockPacketScanIncludesTheExistingBundleHandler()
    {
        var services = new ServiceCollection();
        Program.AddAssignableTo<IPacketEvent>(services, typeof(GetBundleDiscountRulesetEvent).Assembly);
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<GetBundleDiscountRulesetEvent>());
    }

    [Fact]
    public void InstalledProfilePreservesEveryActiveLegacyKeyAndExistingBundleDestination()
    {
        var legacy = LoadRevision("OCTANE-3-6-0-FLOOR-20260909.json");
        var selected = LoadRevision(ProfileName + ".json");
        selected.BuildMappings(InternalRevision());

        Assert.Equal(ProfileName, selected.Name);
        Assert.True(selected.ZeroHeaderIsValid);
        var baseline = LoadBaseline("OCTANE-3-6-0-FLOOR-20260909.json");
        Assert.Equal(411, baseline.IncomingHeaders.Count);
        Assert.Equal(381, baseline.OutgoingHeaders.Count);
        Assert.Equal(legacy.IncomingHeaders.Keys.Order(), selected.IncomingHeaders.Keys.Order());
        Assert.Equal(legacy.OutgoingHeaders.Keys.Where(key => InternalRevision().OutgoingHeaders[key] > 0).Order(), selected.OutgoingHeaders.Keys.Order());
        Assert.Equal(baseline.IncomingHeaders.Count + IncomingExtensions.Count, selected.IncomingIdToInternalIdMapping.Count);
        Assert.Equal(baseline.OutgoingHeaders.Count + OutgoingExtensions.Count - 4, selected.InternalIdToOutgoingIdMapping.Count);
        Assert.Equal(ClientPacketHeader.InfoRetrieveEvent, selected.IncomingIdToInternalIdMapping[0]);
        Assert.Equal(ClientPacketHeader.ClientHelloEvent, selected.IncomingIdToInternalIdMapping[4000]);
    }

    [Fact]
    public void EveryPairedRequestPreservesPlusDestinationAndOpaquePayload()
    {
        var legacy = LoadRevision("OCTANE-3-6-0-FLOOR-20260909.json");
        var selected = LoadRevision(ProfileName + ".json");
        var internalRevision = InternalRevision();
        selected.BuildMappings(internalRevision);
        var overlay = LoadOverlay();
        var server = new RecordingServer();
        var client = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient) { Revision = selected };
        var payload = Payload();
        var checkedRequests = 0;

        foreach (var (key, legacyRendererId) in legacy.IncomingHeaders) {
            // These eight baseline keys have no current effective Renderer composer, so they remain backend-only.
            if (BackendOnlyRequests.Contains(key)) {
                continue;
            }

            Assert.True(overlay.Outgoing.TryGetValue(legacyRendererId, out var wire), key);
            Assert.Equal(selected.IncomingHeaders[key], wire);
            Assert.Equal(internalRevision.IncomingHeaders[key], selected.IncomingIdToInternalIdMapping[wire]);
            ReceiveAndAssert(client, server, wire, internalRevision.IncomingHeaders[key], payload);
            checkedRequests++;
        }

        Assert.Equal(1090u, overlay.Outgoing[1195]);
        Assert.Equal(2697u, overlay.Outgoing[223]);
        Assert.Equal(legacy.IncomingHeaders.Count - BackendOnlyRequests.Count, checkedRequests);
        Assert.Equal(ClientPacketHeader.GetCatalogModeEvent, selected.IncomingIdToInternalIdMapping[overlay.Outgoing[1195]]);
        Assert.Equal(ClientPacketHeader.GetBundleDiscountRulesetEvent, selected.IncomingIdToInternalIdMapping[overlay.Outgoing[223]]);
        Assert.Equal(9910u, ClientPacketHeader.GetBundleDiscountRulesetEvent);
        Assert.Equal(ClientPacketHeader.GetCatalogModeEvent,
            selected.IncomingIdToInternalIdMapping[selected.IncomingHeaders[nameof(ClientPacketHeader.GetCatalogModeEvent)]]);
    }

    [Fact]
    public void EveryPairedResponsePreservesRendererDestinationAndSerializedPayload()
    {
        var internalRevision = InternalRevision();
        var legacy = LoadRevision("OCTANE-3-6-0-FLOOR-20260909.json");
        var selected = LoadRevision(ProfileName + ".json");
        legacy.BuildMappings(internalRevision);
        selected.BuildMappings(internalRevision);
        var overlay = LoadOverlay();
        var legacyFrames = new List<byte[]>();
        var selectedFrames = new List<byte[]>();
        var legacyClient = SendingClient(legacy, legacyFrames);
        var selectedClient = SendingClient(selected, selectedFrames);
        var checkedResponses = 0;

        foreach (var (key, legacyRendererId) in legacy.OutgoingHeaders) {
            // Internal-zero constants are disabled; these three active baseline responses have no current Renderer consumer.
            if (internalRevision.OutgoingHeaders[key] == 0 || BackendOnlyResponses.Contains(key)) {
                continue;
            }

            var internalId = internalRevision.OutgoingHeaders[key];
            var wire = selected.InternalIdToOutgoingIdMapping[internalId];
            Assert.True(overlay.Incoming.TryGetValue(wire, out var rendererId), key);
            Assert.Equal(legacyRendererId, rendererId);
            var composer = new PayloadComposer(internalId);
            legacyClient.Send(composer);
            selectedClient.Send(composer);
            var oldFrame = legacyFrames[^1];
            var newFrame = selectedFrames[^1];
            var decoded = selectedClient.GetMessageIdAndPacketLength(newFrame);
            Assert.True(decoded.Complete);
            Assert.False(decoded.Malformed);
            Assert.Equal(wire, decoded.MessageId);
            Assert.Equal(oldFrame.Length, newFrame.Length);
            Assert.Equal(oldFrame.AsSpan(0, 4).ToArray(), newFrame.AsSpan(0, 4).ToArray());
            Assert.Equal(oldFrame.AsSpan(6).ToArray(), newFrame.AsSpan(6).ToArray());
            Assert.Equal(Payload(), newFrame.AsSpan(6).ToArray());
            checkedResponses++;
        }

        Assert.Equal(legacy.OutgoingHeaders.Count(key => internalRevision.OutgoingHeaders[key.Key] > 0 && !BackendOnlyResponses.Contains(key.Key)), checkedResponses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActualHelloSelectsOptInProfileBeforeConcatenatedZeroRequest(bool knownBuild)
    {
        var selected = LoadRevision(ProfileName + ".json");
        var internalRevision = InternalRevision();
        selected.BuildMappings(internalRevision);
        internalRevision.IncomingIdToInternalIdMapping = new Dictionary<uint, uint> { [4000] = ClientPacketHeader.ClientHelloEvent };
        var cache = new TestCache(internalRevision, selected);
        var hello = new ClientHelloEvent(cache, NullLogger<ClientHelloEvent>.Instance);
        var server = new RecordingServer(hello);
        var disconnected = 0;
        var client = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = internalRevision,
            DisconnectRequested = () => disconnected++
        };
        using var stream = PlusMemoryStream.GetStream();
        var packet = new FlashOutgoingPacket(stream);
        packet.WriteString(knownBuild ? ProfileName : "unknown-opt-in-profile");
        packet.WriteString("HTML5");
        packet.WriteInt(17);
        packet.WriteInt(-42);
        var helloFrame = stream.GetBuffer().AsMemory(0, (int)stream.Length).ToArray();
        client.CreateHeader(helloFrame, 4000);
        var zeroPayload = Payload();
        var input = helloFrame.Concat(Frame(client, 0, zeroPayload)).ToArray();

        client.OnReceived(input, 0, input.Length);

        Assert.Equal(knownBuild ? 0 : 1, disconnected);
        Assert.Same(knownBuild ? selected : internalRevision, client.Revision);
        Assert.Equal(knownBuild ? new[] { ClientPacketHeader.ClientHelloEvent, ClientPacketHeader.InfoRetrieveEvent }
            : new[] { ClientPacketHeader.ClientHelloEvent }, server.Packets.Select(p => p.Id));
        Assert.Equal(helloFrame.AsSpan(6).ToArray(), server.Packets[0].Payload);

        if (knownBuild) {
            Assert.Equal(zeroPayload, server.Packets[1].Payload);
        }

        Assert.Null(client._incompleteStream);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidOptInMappingsAreRejectedBeforePublication(bool incoming)
    {
        var internalRevision = InternalRevision();

        foreach (var failure in new[] { "unknown", "duplicate", "duplicate zero", "16-bit" }) {
            var selected = LoadRevision(ProfileName + ".json");
            var headers = (incoming ? selected.IncomingHeaders : selected.OutgoingHeaders).ToDictionary(p => p.Key, p => p.Value);
            var keys = headers.Keys.Take(2).ToArray();

            if (failure == "unknown") {
                headers.Add("NoSuchPacketKey", 12345);
            }
            else if (failure == "16-bit") {
                headers[keys[0]] = 65536;
            }
            else if (failure == "duplicate zero") {
                headers[keys[0]] = headers[keys[1]] = 0;
            }
            else {
                headers[keys[0]] = headers[keys[1]];
            }

            if (incoming) {
                selected.IncomingHeaders = headers;
            }
            else {
                selected.OutgoingHeaders = headers;
            }

            var exception = Assert.Throws<InvalidOperationException>(() => selected.BuildMappings(internalRevision));
            Assert.Contains(failure == "duplicate zero" ? "duplicate" : failure, exception.Message);
            Assert.Null(selected.IncomingIdToInternalIdMapping);
            Assert.Null(selected.InternalIdToOutgoingIdMapping);
        }
    }

    [Fact]
    public void UnknownWireDoesNotDispatchAndUnknownInternalResponseDoesNotSend()
    {
        var selected = LoadRevision(ProfileName + ".json");
        selected.BuildMappings(InternalRevision());
        var server = new RecordingServer();
        var frames = new List<byte[]>();
        var client = new FlashGameClient(server, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = selected,
            SendCallback = args => { frames.Add(args.MemoryBuffer.ToArray()); return false; }
        };
        var unknown = (uint)Enumerable.Range(0, ushort.MaxValue + 1).First(id => !selected.IncomingIdToInternalIdMapping.ContainsKey((uint)id));
        var input = Frame(client, unknown, Payload());
        client.OnReceived(input, 0, input.Length);
        Assert.Empty(server.Packets);
        Assert.Throws<KeyNotFoundException>(() => client.Send(new PayloadComposer(uint.MaxValue)));
        Assert.Empty(frames);
    }

    [Fact]
    public async Task ActualCacheStartupLoadsOptInAndAllLegacyProfilesTogether()
    {
        var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, "revisions"));

        foreach (var file in new[] { "1.6.6.json", "3.6.0.json", "OCTANE-3-6-0-FLOOR-20260909.json", ProfileName + ".json" }) {
            File.Copy(RevisionPath(file), Path.Combine(temporary, "revisions", file));
        }

        try {
            var cache = new RevisionsCache();
            typeof(RevisionsCache).GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(cache, Path.Combine(temporary, "revisions"));
            await cache.Start();
            Assert.Equal(5, cache.Revisions.Count);
            var selected = cache.Revisions[ProfileName];
            Assert.Equal(ClientPacketHeader.InfoRetrieveEvent, selected.IncomingIdToInternalIdMapping[0]);
            Assert.Equal(ClientPacketHeader.ClientHelloEvent, selected.IncomingIdToInternalIdMapping[4000]);

            foreach (var file in new[] { "1.6.6.json", "3.6.0.json", "OCTANE-3-6-0-FLOOR-20260909.json" }) {
                var legacy = LoadRevision(file);
                var loaded = cache.Revisions[legacy.Name];

                foreach (var (key, wire) in legacy.IncomingHeaders.Where(p => p.Value > 0)) {
                    Assert.Equal(cache.InternalRevision.IncomingHeaders[key], loaded.IncomingIdToInternalIdMapping[wire]);
                }

                foreach (var (key, wire) in legacy.OutgoingHeaders.Where(p => p.Value > 0)) {
                    Assert.Equal(wire, loaded.InternalIdToOutgoingIdMapping[cache.InternalRevision.OutgoingHeaders[key]]);
                }
            }
        }
        finally {
            Directory.Delete(temporary, true);
        }
    }

    [Theory]
    [InlineData("1.6.6.json", "9d9fc4c9d85a673cf658c04728c335fbed76d8542e640be858b2671a9c349390")]
    [InlineData("3.6.0.json", "fd0bd91c8a85e2c050402c9bc69b94e529749b09eb6f088d2d893d34d39e0fdd")]
    [InlineData("OCTANE-3-6-0-FLOOR-20260909.json", "90be589fdb859736dc094340d0d5f33f1cdb4326d79fc3a9eb5dc040875c07c4")]
    [InlineData("example.json", "39fbcf7f7d52ac225b820751bcd97703c47625bdc8423b243c3a89984ab56a6c")]
    [InlineData(ProfileName + ".json", "518b3ef65ea4a6e74fec9377e0cf3d7443b431288872522400ff8af71569203e")]
    [InlineData("AirIdProfileRenderer.json", "9fbebe399b8e6321c09824f64a0244ee7c4351f0cf7da3dcdc49eb96855fc1ed")]
    public void PinnedProfileBaselinesRemainByteExact(string file, string expectedSha256) =>
        Assert.Equal(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(BaselinePath(file)))));

    [Theory]
    [InlineData("1.6.6.json")]
    [InlineData("3.6.0.json")]
    [InlineData("OCTANE-3-6-0-FLOOR-20260909.json")]
    [InlineData("example.json")]
    public void CurrentProfilesPreserveEveryBaselineMappingAndOnlyDeclareBoundedAdapters(string file)
    {
        var baseline = LoadBaseline(file);
        var current = LoadRevision(file);
        Assert.Equal(baseline.Name, current.Name);
        Assert.Equal(baseline.ZeroHeaderIsValid, current.ZeroHeaderIsValid);
        AssertHeaders(baseline.IncomingHeaders, current.IncomingHeaders, IncomingExtensions);
        AssertHeaders(baseline.OutgoingHeaders, current.OutgoingHeaders, OutgoingExtensions);
        current.BuildMappings(InternalRevision()); // Reject duplicate wires/internal destinations and unknown packet keys.
    }

    [Fact]
    public void InstalledProfileAndOverlayPreservePinnedAssignmentsWithOnlyExplicitExtensions()
    {
        var baseline = LoadBaseline(ProfileName + ".json");
        var current = LoadRevision(ProfileName + ".json");
        Assert.Equal(baseline.Name, current.Name);
        Assert.Equal(baseline.ZeroHeaderIsValid, current.ZeroHeaderIsValid);
        AssertHeaders(baseline.IncomingHeaders, current.IncomingHeaders, AirExtensions(incoming: true));
        AssertHeaders(baseline.OutgoingHeaders, current.OutgoingHeaders, AirExtensions(incoming: false));
        current.BuildMappings(InternalRevision());
        var oldOverlay = LoadOverlay(BaselinePath("AirIdProfileRenderer.json"));
        var overlay = LoadOverlay();
        Assert.Equal(oldOverlay.Outgoing.Concat(ExactRequestWires).OrderBy(pair => pair.Key), overlay.Outgoing.OrderBy(pair => pair.Key));
        Assert.Equal(oldOverlay.Incoming.Concat(NewResponseWires).OrderBy(pair => pair.Key), overlay.Incoming.OrderBy(pair => pair.Key));
        Assert.Equal(overlay.Outgoing.Count, overlay.Outgoing.Values.Distinct().Count());
        Assert.Equal(overlay.Incoming.Count, overlay.Incoming.Values.Distinct().Count());

        foreach (var wire in NewResponseWires.Where(pair => pair.Key is 9480 or 9481 or 9482)) {
            // V2 parsers inspect the original wrapper.header; routing must preserve its exact header identity.
            Assert.Equal(wire.Key, wire.Value);
            Assert.Equal(wire.Key, current.InternalIdToOutgoingIdMapping[wire.Value]);
        }
    }

    // Immutable originals come from ddb3d1d9f14d74b6db781c2a51dcd90c27f786e4. Additions are Octane adapters, not native AIR schemas.
    private static readonly Dictionary<string, uint> IncomingExtensions = new()
    {
        [nameof(ClientPacketHeader.WiredChestLockEvent)] = 9329,
        [nameof(ClientPacketHeader.ChestUpgradeEvent)] = 9317,
        [nameof(ClientPacketHeader.ChestOpenEvent)] = 9327,
        [nameof(ClientPacketHeader.ChestCloseEvent)] = 9339,
        [nameof(ClientPacketHeader.ChestStartDepositEvent)] = 9324,
        [nameof(ClientPacketHeader.ChestDepositInventoryItemEvent)] = 9325,
        [nameof(ClientPacketHeader.ChestWithdrawAllEvent)] = 9326,
        [nameof(ClientPacketHeader.ChestWithdrawFurniEvent)] = 9320,
        [nameof(ClientPacketHeader.ChestWithdrawCoinsEvent)] = 9314,
        [nameof(ClientPacketHeader.ChestDepositCoinsEvent)] = 9313,
        [nameof(ClientPacketHeader.ChestSaveOptionsEvent)] = 9338,
        [nameof(ClientPacketHeader.ChestSavePreferencesEvent)] = 9315,
        [nameof(ClientPacketHeader.ChestEnableWiredEvent)] = 9345,
        [nameof(ClientPacketHeader.ChestSaveNotificationsEvent)] = 9316,
        [nameof(ClientPacketHeader.WiredChestOfferItemsEvent)] = 9335,
        [nameof(ClientPacketHeader.WiredChestAcceptEvent)] = 9336,
        [nameof(ClientPacketHeader.WiredChestCancelEvent)] = 9337,
        [nameof(ClientPacketHeader.WiredUserVariableUpdate64Event)] = 10110,
        [nameof(ClientPacketHeader.WiredVariableInspectionRequestEvent)] = 10112,
        [nameof(ClientPacketHeader.WiredUserVariableManage64Event)] = 10111
    };
    private static readonly Dictionary<string, uint> OutgoingExtensions = new()
    {
        [nameof(ServerPacketHeader.AreaHideComposer)] = 6001,
        [nameof(ServerPacketHeader.WiredChestLockComposer)] = 9329,
        [nameof(ServerPacketHeader.WiredChestSettingsAckComposer)] = 9347,
        [nameof(ServerPacketHeader.WiredChestUpgradeComposer)] = 9335,
        [nameof(ServerPacketHeader.WiredChestRewardComposer)] = 9346,
        [nameof(ServerPacketHeader.WiredChestContentsComposer)] = 9312,
        [nameof(ServerPacketHeader.WiredChestFurniChunkComposer)] = 9322,
        [nameof(ServerPacketHeader.WiredChestTradeOpenComposer)] = 9331,
        [nameof(ServerPacketHeader.WiredChestTradeItemsComposer)] = 9332,
        [nameof(ServerPacketHeader.WiredChestTradeCancelledComposer)] = 9333,
        [nameof(ServerPacketHeader.WiredChestTradeCompletedComposer)] = 9334,
        [nameof(ServerPacketHeader.WiredUserVariablesData64Composer)] = 9480,
        [nameof(ServerPacketHeader.WiredVariableHolders64Composer)] = 9481,
        [nameof(ServerPacketHeader.WiredVariableHoldersPage64Composer)] = 9482,
        [nameof(ServerPacketHeader.WiredVariableInspectionDataComposer)] = 9483,
        [nameof(ServerPacketHeader.WiredEnvironmentComposer)] = 347
    };
    private static readonly Dictionary<uint, uint> ExactRequestWires = new() { [10110] = 10110, [10111] = 10111, [10112] = 10112 };
    private static readonly Dictionary<uint, uint> NewResponseWires = new() { [9346] = 9346, [9347] = 9347, [9480] = 9480, [9481] = 9481, [9482] = 9482, [9483] = 9483 };
    private static Dictionary<string, uint> AirExtensions(bool incoming)
    {
        var baseline = LoadOverlay(BaselinePath("AirIdProfileRenderer.json"));

        return (incoming ? IncomingExtensions : OutgoingExtensions).ToDictionary(pair => pair.Key, pair => incoming
            ? ExactRequestWires.TryGetValue(pair.Value, out var requestWire) ? requestWire : baseline.Outgoing[pair.Value]
            : NewResponseWires.TryGetValue(pair.Value, out var responseWire) ? responseWire : baseline.Incoming.Single(old => old.Value == pair.Value).Key);
    }
    private static void AssertHeaders(IReadOnlyDictionary<string, uint> baseline, IReadOnlyDictionary<string, uint> current,
        Dictionary<string, uint> additions) => Assert.Equal(baseline.Concat(additions).OrderBy(pair => pair.Key), current.OrderBy(pair => pair.Key));
    private static string BaselinePath(string file) => Path.Combine(EmulatorRoot, "Plus.Tests", "Fixtures", "LegacyRevisionBaselines", file);
    private static Revision LoadBaseline(string file) => JsonSerializer.Deserialize<Revision>(File.ReadAllText(BaselinePath(file)))!;

    private static readonly HashSet<string> BackendOnlyRequests = new()
    {
        nameof(ClientPacketHeader.GoToFlatEvent), nameof(ClientPacketHeader.InitializeGameCenterEvent),
        nameof(ClientPacketHeader.EventTrackerEvent), nameof(ClientPacketHeader.JoinQueueEvent),
        nameof(ClientPacketHeader.GetGameListEvent), nameof(ClientPacketHeader.OnBullyClickEvent),
        nameof(ClientPacketHeader.GetUserTagsEvent), nameof(ClientPacketHeader.GetGameAchievementsEvent)
    };
    private static readonly HashSet<string> BackendOnlyResponses = new()
    {
        nameof(ServerPacketHeader.SetUniqueIdComposer),
        nameof(ServerPacketHeader.PlayableGamesComposer), nameof(ServerPacketHeader.Game1WeeklyLeaderboardComposer)
    };

    private static string RevisionPath(string file) => Path.Combine(EmulatorRoot, "Resources", "Revisions", file);
    private static Revision LoadRevision(string file) => JsonSerializer.Deserialize<Revision>(File.ReadAllText(RevisionPath(file)))!;
    private static Revision InternalRevision() => new()
    {
        IncomingHeaders = Headers(typeof(ClientPacketHeader)),
        OutgoingHeaders = Headers(typeof(ServerPacketHeader))
    };
    private static Dictionary<string, uint> Headers(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue()!);

    private static (Dictionary<uint, uint> Outgoing, Dictionary<uint, uint> Incoming) LoadOverlay(string? sourcePath = null)
    {
        var path = sourcePath ?? Path.Combine(EmulatorRoot, "Plus.Tests", "Fixtures", "AirIdProfileRenderer.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var profile = document.RootElement.GetProperty("communication.packet.profile");

        return (Map(profile.GetProperty("outgoing")), Map(profile.GetProperty("incoming")));
    }
    private static Dictionary<uint, uint> Map(JsonElement element) => element.EnumerateObject()
        .ToDictionary(property => uint.Parse(property.Name), property => property.Value.GetUInt32());

    private static byte[] Payload()
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = new FlashOutgoingPacket(stream);
        WritePayload(packet);

        return stream.GetBuffer().AsSpan(6, (int)stream.Length - 6).ToArray();
    }
    private static void WritePayload(IOutgoingPacket packet)
    {
        packet.WriteInt(-123456789);
        packet.WriteString("profile\0 café €");
        packet.WriteBool(true);
        packet.WriteShort(-321);
        packet.WriteByte(0xFE);
    }
    private sealed class PayloadComposer(uint id) : IServerPacket
    {
        public uint MessageId => id;
        public void Compose(IOutgoingPacket packet) => WritePayload(packet);
    }
    private static byte[] Frame(FlashGameClient client, uint wire, byte[] payload)
    {
        var frame = new byte[6 + payload.Length];
        payload.CopyTo(frame, 6);
        client.CreateHeader(frame, wire);

        return frame;
    }
    private static void ReceiveAndAssert(FlashGameClient client, RecordingServer server, uint wire, uint expectedInternal, byte[] payload)
    {
        var count = server.Packets.Count;
        var input = Frame(client, wire, payload);
        var original = input.ToArray();
        client.OnReceived(input, 0, input.Length);
        Assert.Equal(count + 1, server.Packets.Count);
        Assert.Equal(expectedInternal, server.Packets[^1].Id);
        Assert.Equal(payload, server.Packets[^1].Payload);
        Assert.Equal(original, input);
    }
    private static FlashGameClient SendingClient(Revision revision, List<byte[]> frames) => new(new RecordingServer(), new FlashPacketFactory(), TestLogging.GameClient)
    {
        Revision = revision,
        SendCallback = args => { frames.Add(args.MemoryBuffer.ToArray()); return false; }
    };
    private sealed class RecordingServer(ClientHelloEvent? hello = null) : IGameServer
    {
        public List<(uint Id, byte[] Payload)> Packets { get; } = new();
        public bool Start() => true;
        public bool Stop() => true;
        public bool ModifyOutgoingPacket(GameClient client, IOutgoingPacket packet) => true;
        public bool HasOutgoingPacketInjectors(uint messageId) => false;
        public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet)
        {
            Packets.Add((messageId, ((FlashIncomingPacket)packet).Buffer.ToArray()));

            return messageId == ClientPacketHeader.ClientHelloEvent && hello is not null
                ? hello.Parse(client, packet) : Task.CompletedTask;
        }
    }
    private sealed class TestCache(Revision internalRevision, Revision selected) : IRevisionsCache
    {
        public IReadOnlyDictionary<string, Revision> Revisions { get; set; } = new Dictionary<string, Revision> { [selected.Name] = selected };
        public Revision InternalRevision { get; } = internalRevision;
    }
}
