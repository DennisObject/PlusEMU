using System.Reflection;
using System.Text.Json;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.FloorPlan;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public class FloorPlanWireTests
{
    [Fact]
    public void NitroRevisionKeepsTheLegacyFloorPlanIds()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RevisionPath("1.6.6.json")));
        var root = document.RootElement;
        var incoming = root.GetProperty("IncomingHeaders");
        var outgoing = root.GetProperty("OutgoingHeaders");

        Assert.Equal(FloorPlanWire.LegacyRevision, root.GetProperty("Name").GetString());
        Assert.Equal(FloorPlanWire.Legacy.GetOccupiedTiles, incoming.GetProperty("GetOccupiedTilesEvent").GetUInt32());
        Assert.Equal(FloorPlanWire.Legacy.GetRoomEntryTile, incoming.GetProperty("GetRoomEntryTileEvent").GetUInt32());
        Assert.Equal(FloorPlanWire.Legacy.UpdateFloorProperties, incoming.GetProperty("UpdateFloorPropertiesEvent").GetUInt32());
        Assert.Equal(FloorPlanWire.Legacy.RoomOccupiedTiles, outgoing.GetProperty("RoomOccupiedTilesComposer").GetUInt32());
        Assert.Equal(FloorPlanWire.Legacy.RoomEntryTile, outgoing.GetProperty("RoomEntryTileComposer").GetUInt32());
        Assert.Equal(FloorPlanWire.Legacy.RoomVisualizationSettings, outgoing.GetProperty("RoomVisualizationSettingsComposer").GetUInt32());
        Assert.Equal(FloorPlanWire.Legacy.FloorHeightMap, outgoing.GetProperty("FloorHeightMapComposer").GetUInt32());
        Assert.Equal(2597u, incoming.GetProperty("GetMarketplaceConfigurationEvent").GetUInt32());
    }

    [Fact]
    public void HybridRevisionRemapsFloorPlanIdsAndTheMarketplaceAlias()
    {
        var legacy = Load("1.6.6.json");
        var hybrid = Load("OCTANE-3-6-0-FLOOR-20260909.json");
        var incomingHeaders = Fields(typeof(ClientPacketHeader));
        var outgoingHeaders = Fields(typeof(ServerPacketHeader));

        Assert.Equal(FloorPlanWire.HybridRevision, hybrid.Name);
        var incoming = Map(hybrid.IncomingHeaders, name => incomingHeaders[name]);
        var outgoing = hybrid.OutgoingHeaders.Where(pair => pair.Value > 0)
            .ToDictionary(pair => outgoingHeaders[pair.Key], pair => pair.Value);

        Assert.Equal(FloorPlanWire.September.GetOccupiedTiles, incoming.Single(pair => pair.Value == ClientPacketHeader.GetOccupiedTilesEvent).Key);
        Assert.Equal(FloorPlanWire.September.GetRoomEntryTile, incoming.Single(pair => pair.Value == ClientPacketHeader.GetRoomEntryTileEvent).Key);
        Assert.Equal(FloorPlanWire.September.UpdateFloorProperties, incoming.Single(pair => pair.Value == ClientPacketHeader.UpdateFloorPropertiesEvent).Key);
        Assert.Equal(FloorPlanWire.MarketplaceAlias, incoming.Single(pair => pair.Value == ClientPacketHeader.GetMarketplaceConfigurationEvent).Key);

        var occupied = new RoomOccupiedTilesComposer(Array.Empty<(int X, int Y)>());
        var entry = new RoomEntryTileComposer(3, 4, 2);
        var height = new FloorHeightMapComposer("0", -1);
        Assert.Equal(FloorPlanWire.September.RoomOccupiedTiles, outgoing[occupied.MessageId]);
        Assert.Equal(FloorPlanWire.September.RoomEntryTile, outgoing[entry.MessageId]);
        Assert.Equal(FloorPlanWire.September.RoomVisualizationSettings, outgoing[ServerPacketHeader.RoomVisualizationSettingsComposer]);
        Assert.Equal(FloorPlanWire.September.FloorHeightMap, outgoing[height.MessageId]);

        var changedIncoming = new HashSet<string>
        {
            "GetOccupiedTilesEvent",
            "GetRoomEntryTileEvent",
            "UpdateFloorPropertiesEvent",
            "GetMarketplaceConfigurationEvent",
            // Reward tracks exist on the Octane client only; RewardTrackTests pins their ids.
            "GetRewardTracksEvent",
            "ClaimRewardTrackPrizeEvent",
            "PurchaseRewardTrackPremiumEvent"
        };
        var changedOutgoing = new HashSet<string>
        {
            "UpdateMagicTileComposer",
            "RoomOccupiedTilesComposer",
            "RoomEntryTileComposer",
            "RoomVisualizationSettingsComposer",
            "FloorHeightMapComposer",
            "RewardTracksComposer",
            "RewardTrackClaimResultComposer",
            "RewardTrackProgressComposer",
            "RewardTrackPremiumPurchaseResultComposer"
        };
        Assert.Equal(legacy.IncomingHeaders.Keys.OrderBy(key => key), hybrid.IncomingHeaders.Keys.OrderBy(key => key));
        Assert.Equal(legacy.OutgoingHeaders.Keys.OrderBy(key => key), hybrid.OutgoingHeaders.Keys.OrderBy(key => key));
        foreach (var (name, wire) in legacy.IncomingHeaders)
        {
            if (!changedIncoming.Contains(name))
                Assert.Equal(wire, hybrid.IncomingHeaders[name]);
        }

        foreach (var (name, wire) in legacy.OutgoingHeaders)
        {
            if (!changedOutgoing.Contains(name))
                Assert.Equal(wire, hybrid.OutgoingHeaders[name]);
        }
    }

    [Fact]
    public void MarketplaceAliasSurvivesTheUnsignedShortHeader()
    {
        var memory = new byte[2];
        FlashGameClient.EncodeInt16(memory, unchecked((short)FloorPlanWire.MarketplaceAlias), 0);

        Assert.Equal(FloorPlanWire.MarketplaceAlias, (uint)FlashGameClient.DecodeInt16(memory));
        Assert.DoesNotContain(Fields(typeof(ClientPacketHeader)).Values, header => header is 65001 or 65002 or 65003);
        Assert.DoesNotContain(Fields(typeof(ServerPacketHeader)).Values, header => header is 65001 or 65002 or 65003);
    }

    [Fact]
    public void StockRendererRevisionKeepsTheSupportedDefaultMapping()
    {
        var supported = Load("1.6.6.json");
        var stock = Load("3.6.0.json");
        var floor = Load("OCTANE-3-6-0-FLOOR-20260909.json");
        var incomingHeaders = Fields(typeof(ClientPacketHeader));
        var outgoingHeaders = Fields(typeof(ServerPacketHeader));

        Assert.Equal(FloorPlanWire.LegacyRevision, supported.Name);
        Assert.Equal(FloorPlanWire.StockRendererRevision, stock.Name);
        Assert.Equal(FloorPlanWire.HybridRevision, floor.Name);
        Assert.NotEqual(stock.Name, floor.Name);
        Assert.Equal(supported.IncomingHeaders.Keys.OrderBy(key => key), stock.IncomingHeaders.Keys.OrderBy(key => key));
        Assert.Equal(supported.OutgoingHeaders.Keys.OrderBy(key => key), stock.OutgoingHeaders.Keys.OrderBy(key => key));
        var rewardHeaders = new HashSet<string>
        {
            "GetRewardTracksEvent",
            "ClaimRewardTrackPrizeEvent",
            "PurchaseRewardTrackPremiumEvent",
            "RewardTracksComposer",
            "RewardTrackClaimResultComposer",
            "RewardTrackProgressComposer",
            "RewardTrackPremiumPurchaseResultComposer"
        };
        foreach (var (name, wire) in supported.IncomingHeaders)
        {
            if (!rewardHeaders.Contains(name))
                Assert.Equal(wire, stock.IncomingHeaders[name]);
        }
        foreach (var (name, wire) in supported.OutgoingHeaders)
        {
            if (!rewardHeaders.Contains(name))
                Assert.Equal(wire, stock.OutgoingHeaders[name]);
        }
        Assert.All(stock.IncomingHeaders.Keys, name => Assert.Contains(name, incomingHeaders.Keys));
        Assert.All(stock.OutgoingHeaders.Keys, name => Assert.Contains(name, outgoingHeaders.Keys));
        Assert.Equal(stock.IncomingHeaders.Values.Where(wire => wire > 0).Count(), stock.IncomingHeaders.Values.Where(wire => wire > 0).Distinct().Count());
        Assert.Equal(stock.OutgoingHeaders.Values.Where(wire => wire > 0).Count(), stock.OutgoingHeaders.Values.Where(wire => wire > 0).Distinct().Count());
    }

    [Fact]
    public void FloorHeightMapWritesTheHideAndCameraTail()
    {
        var packet = new RecordingPacket();
        var hide = new FloorHeightMapComposer.AreaHide(9, true, 1, 2, 3, 4, false);

        new FloorHeightMapComposer("0x\r00", -1, true, new[] { hide }, 5, 6, 1.5f).Compose(packet);

        Assert.Equal(new object[]
        {
            true, -1, "0x\r00", 1,
            9, true, 1, 2, 3, 4, false,
            5, 6, BitConverter.SingleToInt32Bits(1.5f)
        }, packet.Writes);

        var empty = new RecordingPacket();
        new FloorHeightMapComposer("0", -1).Compose(empty);
        Assert.Equal(new object[] { true, -1, "0", 0, 0, 0, 0 }, empty.Writes);
    }

    private static Dictionary<uint, uint> Map(IReadOnlyDictionary<string, uint> headers, Func<string, uint> internalId) =>
        headers.Where(pair => pair.Value > 0).ToDictionary(pair => pair.Value, pair => internalId(pair.Key));

    private static Dictionary<string, uint> Fields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .ToDictionary(field => field.Name, field => (uint)field.GetRawConstantValue()!);

    private static RevisionFile Load(string fileName)
    {
        var revision = JsonSerializer.Deserialize<RevisionFile>(File.ReadAllText(RevisionPath(fileName)), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return revision ?? throw new InvalidOperationException(fileName);
    }

    private static string RevisionPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Resources", "Revisions", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(fileName);
    }

    private sealed class RevisionFile
    {
        public string Name { get; set; } = "";
        public Dictionary<string, uint> IncomingHeaders { get; set; } = new();
        public Dictionary<string, uint> OutgoingHeaders { get; set; } = new();
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = new();
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value ?? "");
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
