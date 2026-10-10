using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Buffers.Binary;
using Plus.Communication.Flash;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Users;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredNativeCatalogTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void NativeRequestsDispatchAtTheirDirectCanonicalIds()
    {
        var hash = new WiredAllVariablesRequestEvent(new WiredVariableMenuService());
        var diff = new WiredVariableHashesEvent(new WiredVariableMenuService());
        using var manager = new PacketManager([hash, diff], NullLogger<PacketManager>.Instance);
        var registered = (Dictionary<uint, IPacketEvent>)typeof(PacketManager).GetField("_incomingPackets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        Assert.Same(hash, registered[1327]);
        Assert.Same(diff, registered[3698]);
        Assert.Equal(3478u, ServerPacketHeader.WiredAllVariablesHashComposer);
        Assert.Equal(3058u, ServerPacketHeader.WiredAllVariablesDiffComposer);
    }

    [Fact]
    public void NativeOwningUserTypeIsIndependentOfItsTarget()
    {
        var variable = new WiredVariableDescription(new(10, 1, 5, "score", WiredVariableTarget.User, WiredVariableAvailability.Persistent, true), true, false);
        var packet = new Packet();
        var endpoint = new WiredNativeEndpoint(variable, variable.Definition, null, true, false);
        var native = WiredNativeCatalogProjection.Project(endpoint, null);
        new WiredNativeCatalogDiffComposer(new(42, true, [], [native])).Compose(packet);
        Assert.Equal(0, packet.Values[6]);
        Assert.Equal(1, packet.Values[9]);
    }

    [Fact]
    public void HashBodyIsExactlyOneSignedInteger()
    {
        var packet = new Packet();
        new WiredAllVariablesHashComposer(int.MinValue).Compose(packet);
        Assert.Equal(new object[] { int.MinValue }, packet.Values);
    }

    [Theory]
    [InlineData(WiredVariableTarget.Furni, 0, WiredVariableAvailability.RoomActive, false)]
    [InlineData(WiredVariableTarget.User, 1, WiredVariableAvailability.UserActive, false)]
    [InlineData(WiredVariableTarget.User, 1, WiredVariableAvailability.Shared, false)]
    [InlineData(WiredVariableTarget.Global, -10, WiredVariableAvailability.Persistent, true)]
    [InlineData(WiredVariableTarget.Context, -20, WiredVariableAvailability.RoomActive, false)]
    public void OrdinaryCapabilitiesFollowActualMutationAndLifetimeDomains(WiredVariableTarget target, int wireTarget,
        WiredVariableAvailability availability, bool always)
    {
        var definition = new WiredVariableDefinition(10, 42, 1, "score", target, availability, true);
        var directory = new Directory();
        directory.Rows[10] = definition;
        var module = Module(directory);
        var endpoint = Assert.Single(module.DescribeNativeDefinitions([definition], new Dictionary<WiredVariableReference, WiredVariableDerivation>(), [])!);
        var variable = WiredNativeCatalogProjection.Project(endpoint, null);
        Assert.Equal(0, variable.Type);
        Assert.Equal(wireTarget, variable.Target);
        Assert.Equal(always, variable.AlwaysAvailable);
        Assert.Equal(target != WiredVariableTarget.Global, variable.CanCreateAndDelete);
        Assert.True(variable.CanWriteValue);
        Assert.True(variable.CanInterceptChanges);
        Assert.True(variable.CanReadCreationTime);
        Assert.Equal(target == WiredVariableTarget.Context ? 999 : (int)availability, variable.Availability);
        Assert.Empty(module.DrainChanges());
    }

    [Theory]
    [InlineData(WiredVariableTarget.Furni, "~area_hide.hiding_wallitems", false, true)]
    [InlineData(WiredVariableTarget.Furni, "@altitude", true, false)]
    [InlineData(WiredVariableTarget.Furni, "@id", false, false)]
    [InlineData(WiredVariableTarget.User, "@handitem", true, false)]
    [InlineData(WiredVariableTarget.User, "@achievement_score", false, false)]
    [InlineData(WiredVariableTarget.Global, "@teams.red.score", true, false)]
    [InlineData(WiredVariableTarget.Context, "@event.chat.style", false, false)]
    [InlineData(WiredVariableTarget.User, "@not_implemented", false, false)]
    public void AuthorizedBuiltinAliasesRetainTruthfulFiniteCapabilities(WiredVariableTarget target, string token, bool write, bool presence)
    {
        var definition = new WiredVariableDefinition(10, 42, 1, "alias", target, WiredVariableAvailability.RoomActive, true,
            Link: new(42, new(target, "internal:" + token), false));
        var directory = new Directory();
        directory.Rows[10] = definition;
        var endpoint = Assert.Single(Module(directory).DescribeNativeDefinitions([definition], new Dictionary<WiredVariableReference, WiredVariableDerivation>(), [])!);
        var variable = WiredNativeCatalogProjection.Project(endpoint, null);
        Assert.Equal(2, variable.Type);
        Assert.Equal(write, variable.CanWriteValue);
        Assert.Equal(presence, variable.CanCreateAndDelete);
        Assert.False(variable.CanInterceptChanges);
        Assert.False(variable.CanReadCreationTime);
        Assert.False(variable.CanReadLastUpdateTime);
        Assert.False(variable.AlwaysAvailable);
        Assert.Equal(999, variable.Availability);
        Assert.Equal(RoomWiredBuiltinVariables.HasNumericValue(new(target, token)), variable.HasValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedCustomUsesOneDirectoryAndTerminalFactsWithoutAdvertisingInterception(bool readOnly)
    {
        var directory = new Directory();
        var terminal = new WiredVariableDefinition(20, 42, 1, "source", WiredVariableTarget.User, WiredVariableAvailability.Persistent, false);
        var owning = terminal with { ItemId = 10, Name = "alias", HasValue = true, Link = new(42, new(terminal.Target, terminal.Token), readOnly) };
        directory.Rows[10] = owning;
        directory.Rows[20] = terminal;
        var endpoint = Assert.Single(Module(directory).DescribeNativeDefinitions([owning], new Dictionary<WiredVariableReference, WiredVariableDerivation>(), [])!);
        var variable = WiredNativeCatalogProjection.Project(endpoint, null);
        Assert.Equal(2, variable.Type);
        Assert.False(variable.HasValue);
        Assert.False(variable.CanWriteValue);
        Assert.Equal(!readOnly, variable.CanCreateAndDelete);
        Assert.True(variable.CanReadCreationTime);
        Assert.False(variable.CanInterceptChanges);
        Assert.Equal(1, directory.Reads[10]);
        Assert.Equal(1, directory.Reads[20]);
    }

    [Fact]
    public void HashesIncludeRenameCapabilitiesAndAbsentVersusEmptyConnector()
    {
        var variable = Native();
        Assert.NotEqual(variable.Hash, (variable with { Name = "renamed" }).Hash);
        Assert.NotEqual(variable.Hash, (variable with { CanWriteValue = false }).Hash);
        Assert.NotEqual(variable.Hash, (variable with { Connector = [] }).Hash);
        Assert.NotEqual((variable with { Connector = [] }).Hash, (variable with { Connector = [new(1, "one")] }).Hash);
    }

    [Fact]
    public void LargeCatalogDiffUsesFrozenRowsAndOneHashWithRemovalOnlyAndEmptyFinal()
    {
        var variables = Enumerable.Range(0, 205).Select(index => Native() with { Id = "user:" + index }).ToImmutableArray();
        var catalog = new WiredNativeCatalog(variables);
        var chunks = catalog.Diff(new Dictionary<string, int> { ["gone"] = int.MinValue });
        Assert.Equal(new[] { 100, 100, 5 }, chunks.Select(chunk => chunk.Changed.Length));
        Assert.All(chunks, chunk => Assert.Equal(catalog.Hash, chunk.Hash));
        Assert.Equal(new[] { "gone" }, chunks[0].Removed);
        Assert.False(chunks[0].LastChunk);
        Assert.True(chunks[2].LastChunk);
        var known = variables.ToDictionary(variable => variable.Id, variable => variable.Hash);
        var equal = Assert.Single(catalog.Diff(known));
        Assert.Empty(equal.Changed);
        Assert.True(equal.LastChunk);
        known["gone"] = int.MaxValue;
        var removal = Assert.Single(catalog.Diff(known));
        Assert.Equal(new[] { "gone" }, removal.Removed);
        Assert.Empty(removal.Changed);
    }

    [Fact]
    public void RealRoomCaptureUsesCapturedConfigurationNotConnectorCacheAndFreezesItsPairs()
    {
        var fixture = new Fixture();
        fixture.AddDefinition(10);
        var connector = fixture.AddMetadata(20, "wf_xtra_var_text_connector", new() { Text = "2=old" });
        Assert.True(WiredNativeEditorProjection.TryCompile(20, connector.Descriptor, WiredNativeTestSupport.FromLegacyVariableDraft(connector.Descriptor, new() { Text = "1=new" }), out var capturedConfiguration));
        var field = connector.GetType().BaseType!.GetField("<Configuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(connector, capturedConfiguration);
        Assert.Equal("old", connector.TextConnector[2]);
        Assert.True(fixture.Variables.TryCaptureNativeCatalog(out var catalog));
        var native = Assert.Single(catalog!.Variables);
        Assert.Equal(new KeyValuePair<int, string>(1, "new"), Assert.Single(native.Connector!.Value));
        WiredNativeTestSupport.InstallLegacyVariableDraft(connector, new() { Text = "1=after" });
        Assert.Equal("new", Assert.Single(native.Connector.Value).Value);
        Assert.Equal(native.Hash, Assert.Single(catalog.Diff(new Dictionary<string, int>())).Changed[0].Hash);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("load")]
    [InlineData("detach")]
    [InlineData("configuration")]
    [InlineData("unselected_pose")]
    [InlineData("replacement")]
    [InlineData("definition")]
    [InlineData("owner")]
    [InlineData("collision")]
    public void ObservedCandidateChangesRefuseEntireCaptureRatherThanReturnEmpty(string change)
    {
        var fixture = new Fixture();
        var definition = fixture.AddDefinition(10);
        fixture.AddMetadata(20, "wf_xtra_var_text_connector", new() { Text = "1=selected" });
        var unselected = fixture.AddMetadata(21, "wf_xtra_var_text_connector", new() { Text = "1=unselected" });
        fixture.AddDefinition(22, "wf_var_quest", new() { Text = "quest22\tchat" });
        fixture.Directory.OnRead = () =>
        {
            fixture.Directory.OnRead = null;
            Task.Run(() =>
            {
                switch (change) {
                    case "save":
                        fixture.Variables.ConfigurationSaved(definition);
                        break;
                    case "load":
                        fixture.Variables.ConfigurationLoaded(definition);
                        break;
                    case "detach":
                        fixture.Variables.ItemDetached(definition.Item);
                        break;
                    case "configuration":
                        WiredNativeTestSupport.InstallLegacyVariableDraft(definition, new() { IntParams = [1, 10], Text = "changed" });
                        break;
                    case "unselected_pose":
                        unselected.Item.SetState(1, 0, 0, []);
                        break;
                    case "replacement":
                        fixture.Floor[10] = new() { Id = 10, RoomId = 42 };
                        break;
                    case "definition":
                        definition.Item.Definition.Id++;
                        break;
                    case "owner":
                        fixture.World.Room.OwnerId = 2;
                        break;
                    case "collision":
                        var id = WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 22, 0, false)!.Value;
                        fixture.Floor[id] = new() { Id = id, RoomId = 42 };
                        break;
                }
            }).GetAwaiter().GetResult();
        };
        Assert.False(fixture.Variables.TryCaptureNativeCatalog(out var catalog));
        Assert.Null(catalog);
    }

    [Fact]
    public void DerivedRowsAreActualEntryPointsAndAliasToSyntheticDoesNotInventMembership()
    {
        var fixture = new Fixture();
        fixture.AddDefinition(10);
        fixture.AddDefinition(20, "wf_var_quest", new() { Text = "quest20\tchat" });
        var id = WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 10, 0, false)!.Value;
        fixture.AddDefinition(11, "wf_var_echo", new() { Text = "{\"variableName\":\"unreachable\",\"sourceTargetType\":0,\"sourceVariableToken\":\"custom:" + id + "\"}" });
        Assert.True(fixture.Variables.TryCaptureNativeCatalog(out var catalog));
        Assert.Equal(7, catalog!.Variables.Length); // the user definition, the quest definition and its five derived keys
        Assert.DoesNotContain(catalog.Variables, variable => variable.Id == "user:11");
        Assert.All(catalog.Variables.Where(variable => variable.Type == 3), variable =>
        {
            Assert.True(variable.HasValue);
            Assert.False(variable.CanWriteValue);
            Assert.False(variable.CanCreateAndDelete);
            Assert.False(variable.CanReadCreationTime);
            Assert.False(variable.AlwaysAvailable);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentRoomAndPermissionAreRecheckedBetweenActualChunks(bool loseRights)
    {
        var fixture = new Fixture();

        for (uint id = 1000; id < 1105; id++) {
            fixture.AddDefinition(id);
        }

        fixture.World.Client.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            fixture.World.Packets.Add((BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));

            if (loseRights) {
                fixture.World.Habbo.Username = "visitor";
                fixture.World.Room.Type = "public";
                var settings = fixture.World.Room.GetWired().Settings;
                typeof(Plus.HabboHotel.Items.Wired.Settings.WiredRoomSettings).GetField("_saved", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(settings, new Plus.HabboHotel.Items.Wired.Settings.WiredRoomSettingsSnapshot(0, 0));
                typeof(Plus.HabboHotel.Items.Wired.Settings.WiredRoomSettings).GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(settings, true);
            }
            else {
                fixture.World.Habbo.CurrentRoom = null;
            }

            return true;
        };
        new WiredVariableMenuService().ShowCatalogDiff(fixture.World.Room, fixture.World.Client, new Dictionary<string, int>());
        Assert.Single(fixture.World.Packets);
        Assert.Equal(3058u, fixture.World.Packets[0].Header);
    }

    [Fact]
    public void CaptureFailureAndAbsentPermissionNeverEmitAuthoritativeEmptyDiff()
    {
        var fixture = new Fixture();
        var definition = fixture.AddDefinition(10);
        fixture.Floor[10] = new() { Id = 10 };
        var service = new WiredVariableMenuService();
        service.ShowCatalogDiff(fixture.World.Room, fixture.World.Client, new Dictionary<string, int> { ["user:10"] = 1 });
        service.ShowCatalogHash(fixture.World.Room, fixture.World.Client);
        Assert.Empty(fixture.World.Packets);
        fixture.Floor[10] = definition.Item;
        fixture.World.Habbo.CurrentRoom = null;
        service.ShowCatalogDiff(fixture.World.Room, fixture.World.Client, new Dictionary<string, int>());
        Assert.Empty(fixture.World.Packets);
    }

    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, true)]
    public void ActualNativeByteFixturesPreserveSignedHashesAndConnectorPresence(int hash, bool present)
    {
        var row = Native() with { Target = -20, Availability = 999, Connector = present ? [] : null };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        client.Send(new WiredNativeCatalogDiffComposer(new(hash, true, ["removed:opaque"], [row])));
        var frame = Assert.Single(sent);
        Assert.Equal(3058u, frame.Header);
        var packet = new FlashIncomingPacket { Buffer = frame.Payload };
        Assert.Equal(hash, packet.ReadInt());
        Assert.True(packet.ReadBool());
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal("removed:opaque", packet.ReadString());
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(row.Hash, packet.ReadInt());
        Assert.Equal(row.Id, packet.ReadString());
        Assert.Equal(0, packet.ReadInt());
        Assert.Equal("score", packet.ReadString());
        Assert.Equal(999, packet.ReadInt());
        Assert.Equal(-20, packet.ReadInt());

        for (var index = 0; index < 8; index++) {
            packet.ReadBool();
        }

        Assert.Equal(present, packet.ReadBool());

        if (present) {
            Assert.Equal(0, packet.ReadInt());
        }

        Assert.False(packet.HasDataRemaining());
        output.WriteLine("NATIVE_CATALOG_BYTES:" + System.Text.Json.JsonSerializer.Serialize(new { hash, present, base64 = Convert.ToBase64String(frame.Payload) }));
    }

    [Fact]
    public void NativeResolutionNeverCallsLiveDerivationProviderAndCapsBeforeSorting()
    {
        var directory = new Directory();
        var definitions = Enumerable.Range(1, 4097).Select(id => new WiredVariableDefinition((uint)id, 42, 1,
            id == 4097 ? "aaa" : "z" + id, WiredVariableTarget.User, WiredVariableAvailability.Persistent, true)).ToArray();

        foreach (var definition in definitions) {
            directory.Rows[definition.ItemId] = definition;
        }

        var module = new WiredVariableModule(42, directory, new MemoryWiredVariableStore(), TimeProvider.System,
            derive: _ => throw new InvalidOperationException("Live derived resolver must not reopen the maps"));
        var rows = module.DescribeNativeDefinitions(definitions, new Dictionary<WiredVariableReference, WiredVariableDerivation>(), []);
        Assert.Equal(4096, rows!.Count);
        Assert.DoesNotContain(rows, row => row.Description.Definition.ItemId == 4097);
    }

    [Fact]
    public void FailedDirectoryDecodeAndDetachedDefinitionRefuseInsteadOfRemovingKnownRows()
    {
        var fixture = new Fixture();
        var owning = fixture.AddDefinition(10);
        fixture.Directory.OnRead = () => throw new InvalidOperationException("Directory read failure");
        Assert.False(fixture.Variables.TryCaptureNativeCatalog(out var catalog));
        Assert.Null(catalog);
        fixture.Directory.OnRead = null;
        fixture.Directory.Rows[10] = fixture.Directory.Rows[10] with { Name = "different" };
        Assert.False(fixture.Variables.TryCaptureNativeCatalog(out catalog));
        fixture.Directory.Rows[10] = fixture.Directory.Rows[10] with { Name = owning.Configuration.Text };
        fixture.Floor.TryRemove(10, out _);
        Assert.False(fixture.Variables.TryCaptureNativeCatalog(out catalog));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CapturedTimeMetadataPreservesSourceValueVersusTimestampEligibility(int mode)
    {
        var fixture = new Fixture();
        fixture.AddDefinition(10);
        fixture.AddMetadata(20, "wf_xtra_var_time_util", new() { IntParams = [1 << 21, mode] });
        Assert.True(fixture.Variables.TryCaptureNativeCatalog(out var catalog));
        var row = Assert.Single(catalog!.Variables, variable => variable.Type == 3);
        Assert.False(row.CanReadCreationTime);
        Assert.False(row.CanWriteValue);
        Assert.Equal("score10.second", row.Name);
    }

    [Fact]
    public void UnauthorizedMembershipOmissionIsDistinctFromDecodeInconsistency()
    {
        var fixture = new Fixture();
        fixture.AddDefinition(10);
        fixture.Directory.Rows[10] = fixture.Directory.Rows[10] with { OwnerId = 2 };
        Assert.True(fixture.Variables.TryCaptureNativeCatalog(out var omitted));
        Assert.Empty(omitted!.Variables);
        fixture.Directory.Rows[10] = fixture.Directory.Rows[10] with { OwnerId = 1, Name = "inconsistent" };
        Assert.False(fixture.Variables.TryCaptureNativeCatalog(out var refused));
        Assert.Null(refused);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void CorruptedCapturedTimeMetadataRefusesTheWholeCatalog(int mode)
    {
        var fixture = new Fixture();
        fixture.AddDefinition(10);
        var metadata = fixture.AddMetadata(20, "wf_xtra_var_time_util", new() { IntParams = [1 << 21, 0] });
        // A corrupted captured configuration can only appear by bypassing the bound publication path.
        typeof(Plus.HabboHotel.Items.Wired.Modern.Addons.WiredConfiguredBehaviorBox).GetField("<Configuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(metadata, new WiredConfiguration { IntParams = [1 << 21, mode] });
        Assert.False(fixture.Variables.TryCaptureNativeCatalog(out var catalog));
        Assert.Null(catalog);
    }

    private static WiredNativeVariable Native() => new("user:10", 0, "score", 10, 1, false, true, true, true, true, false, true, true, null);
    private static WiredVariableModule Module(Directory directory) => new(42, directory, new MemoryWiredVariableStore(), TimeProvider.System,
        new RoomWiredBuiltinVariables(new WiredChestProtocolTests.World().Room));

    private sealed class Directory : IWiredVariableDirectory
    {
        public Dictionary<uint, WiredVariableDefinition> Rows { get; } = [];
        public Dictionary<uint, int> Reads { get; } = [];
        public Action? OnRead;
        public uint? GetRoomOwner(uint id) => id == 42 ? 1u : null;
        public WiredVariableDefinition? Find(uint id)
        {
            Reads[id] = Reads.GetValueOrDefault(id) + 1;
            OnRead?.Invoke();

            return Rows.GetValueOrDefault(id);
        }
    }

    private sealed class Fixture
    {
        public WiredChestProtocolTests.World World { get; } = new();
        public Directory Directory { get; } = new();
        public WiredRoomVariables Variables => World.Room.GetWired().Variables;
        public ConcurrentDictionary<uint, Item> Floor => (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling)
            .GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World.Room.GetRoomItemHandler())!;
        public Fixture()
        {
            World.Habbo.Access = EditorTestSupport.Access([]);
            World.Habbo.Username = "owner";
            World.Room.Type = "private";
            typeof(Plus.HabboHotel.Rooms.Room).GetProperty("OwnerName")!.SetValue(World.Room, "owner");
            var module = new WiredVariableModule(42, Directory, new MemoryWiredVariableStore(), World.Clock, new RoomWiredBuiltinVariables(World.Room));
            typeof(WiredRoomVariables).GetField("<Module>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Variables, module);
            typeof(WiredRoomVariables).GetField("_quests", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Variables,
                new WiredQuestVariableTests.Quests([new Plus.HabboHotel.Quests.Quest(1, "social", 1, Plus.HabboHotel.Quests.QuestType.SocialChat, 5, "chat", 0, "", 0, null, null)]));
        }

        public WiredVariableDefinitionBox AddDefinition(uint id, string name = "wf_var_user", WiredConfiguration? proposed = null)
        {
            Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
            var item = new Item { Id = id, RoomId = 42, OwnerId = 1, Definition = new() { Id = id, InteractionName = name } };
            Floor[id] = item;
            var box = new WiredVariableDefinitionBox(World.Room, item, descriptor);
            WiredNativeTestSupport.InstallLegacyVariableDraft(box, proposed ?? new() { IntParams = [1, 10], Text = "score" + id });
            var configuration = box.Configuration;
            Assert.True(WiredVariableDefinitions.TryDecode(name, id, 42, 1, configuration, out var definition, out _));
            Directory.Rows[id] = definition!;
            Variables.ConfigurationLoaded(box);

            return box;
        }

        public WiredVariableMetadataBox AddMetadata(uint id, string name, WiredConfiguration proposed)
        {
            Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
            var item = new Item { Id = id, RoomId = 42, OwnerId = 1, Definition = new() { Id = id, InteractionName = name } };
            Floor[id] = item;
            var box = new WiredVariableMetadataBox(World.Room, item, descriptor);
            WiredNativeTestSupport.InstallLegacyVariableDraft(box, proposed);
            Variables.ConfigurationLoaded(box);

            return box;
        }
    }

    private sealed class Packet : IOutgoingPacket
    {
        public List<object> Values { get; } = [];
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => default;
        public void WriteByte(byte value) => Values.Add(value);
        public void WriteShort(short value) => Values.Add(value);
        public void WriteInt(int value) => Values.Add(value);
        public void WriteInteger(int value) => Values.Add(value);
        public void WriteUInt(uint value) => Values.Add(value);
        public void WriteUInteger(uint value) => Values.Add(value);
        public void WriteBool(bool value) => Values.Add(value);
        public void WriteBoolean(bool value) => Values.Add(value);
        public void WriteString(string value) => Values.Add(value);
        public void WriteDouble(double value) => Values.Add(value);
    }
}
