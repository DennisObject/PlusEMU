using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.FloorPlan;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public class FloorPlanSaveTests
{
    private static readonly IReadOnlyDictionary<(int X, int Y), FloorPlanSave.FloorTile> Flat =
        new Dictionary<(int X, int Y), FloorPlanSave.FloorTile>
        {
            [(0, 0)] = new(0, true),
            [(1, 0)] = new(0, true),
            [(0, 1)] = new(0, true),
            [(1, 1)] = new(0, true),
            [(2, 0)] = new(0, true),
            [(3, 0)] = new(0, true),
            [(4, 0)] = new(0, true),
            [(3, 4)] = new(0, true),
            [(4, 4)] = new(0, true)
        };

    [Fact]
    public void SaveKeepsTheEditorMapWithoutABlockedBorderRow()
    {
        var decision = Save("00\r00");

        Assert.True(decision.Accepted);
        Assert.Equal("00\r00", decision.Map);
        Assert.Equal(0, decision.DoorZ);
    }

    [Fact]
    public void DoorOnABlockedTileOrOutsideTheMapIsRejected()
    {
        Assert.Equal(FloorPlanSave.ErrorEntryNotOnTile, Save("x0\r00", doorX: 0).Error);
        Assert.Equal(FloorPlanSave.ErrorEntryOutside, Save("00\r00", doorX: 2).Error);
        Assert.Equal(FloorPlanSave.ErrorEntryOutside, Save("00\r00", doorY: -1).Error);
        Assert.Equal(FloorPlanSave.ErrorEntryDirection, Save("00\r00", doorDirection: 8).Error);
    }

    [Fact]
    public void ThicknessAndWallHeightStayInTheClientRange()
    {
        Assert.Equal(FloorPlanSave.ErrorFloorThickness, Save("00\r00", floorThickness: -3).Error);
        Assert.Equal(FloorPlanSave.ErrorWallThickness, Save("00\r00", wallThickness: 2).Error);
        Assert.Equal(FloorPlanSave.ErrorWallHeight, Save("00\r00", wallHeight: 16).Error);

        var decision = Save("a0\r00", wallHeight: -1);
        Assert.True(decision.Accepted);
        Assert.Equal(10, decision.DoorZ);
        Assert.Equal(-1, decision.WallHeight);
        Assert.Equal(-2, decision.WallThickness);
        Assert.Equal(1, decision.FloorThickness);
    }

    [Fact]
    public void EmptyBlockedOrUnevenMapsAreRejected()
    {
        Assert.Equal(FloorPlanSave.ErrorEffectiveHeight, Save("xx\rxx").Error);
        Assert.Equal(FloorPlanSave.ErrorTitle, Save("00\r0").Error);
        Assert.Equal(FloorPlanSave.ErrorTitle, Save("0y\r00").Error);
        Assert.Equal(FloorPlanSave.ErrorTooLargeWidth, Save(new string('0', 65)).Error);
        Assert.Equal(FloorPlanSave.ErrorTooLargeHeight, Save(string.Join('\r', Enumerable.Repeat("0", 65))).Error);
        Assert.True(Save(string.Join('\r', Enumerable.Repeat(new string('0', 64), 64))).Accepted);
    }

    [Fact]
    public void FurnitureOnARemovedRaisedOrMissingTileBlocksTheSave()
    {
        var chair = new FloorPlanSave.FloorPlanItem(4, 0, 0, 0, 1, 1);
        var sofa = new FloorPlanSave.FloorPlanItem(9, 3, 4, 2, 1, 2);

        Assert.Equal(new uint[] { 4 }, Save("x0\r00", doorX: 1, items: new[] { chair }).BlockingItemIds);
        Assert.Equal(new uint[] { 4 }, Save("10\r00", items: new[] { chair }).BlockingItemIds);
        Assert.Equal(new uint[] { 4 }, Save("00\r00", items: new[] { new FloorPlanSave.FloorPlanItem(4, 5, 0, 0, 1, 1) }).BlockingItemIds);
        Assert.Empty(Save("01\r00", items: new[] { chair }).BlockingItemIds);
        var raisedFarTile = string.Join('\r', "00000", "00000", "00000", "00000", "00010");
        Assert.Equal(new uint[] { 9 }, Save(raisedFarTile, doorX: 0, items: new[] { sofa }, tiles: WithHeight(4, 4, 1)).BlockingItemIds);
    }

    [Fact]
    public void OccupiedTilesCoverTheWholeFurnitureFootprint()
    {
        var tiles = FloorPlanSave.OccupiedTiles(new[]
        {
            new FloorPlanSave.FloorPlanItem(9, 3, 4, 2, 1, 2),
            new FloorPlanSave.FloorPlanItem(4, 3, 4, 0, 1, 1)
        });

        Assert.Equal(new[] { (3, 4), (4, 4) }, tiles);
    }

    [Fact]
    public void MapOnlySaveKeepsTheExistingDoorAndThickness()
    {
        var map = "00000\r00000\r00000\r00000\r00000";
        var existing = new FloorPlanSave.Layout(3, 4, 2, -2, 1, 7);
        var body = FloorPlanRequest.Read(Packet(map));
        var layout = FloorPlanSave.Resolve(body.DoorFieldsPresent, body.WallHeightPresent, body.Requested, existing);

        Assert.False(body.DoorFieldsPresent);
        Assert.Equal(existing, layout);

        var decision = FloorPlanSave.Evaluate(map, layout.DoorX, layout.DoorY, layout.DoorDirection, layout.WallThickness, layout.FloorThickness, layout.WallHeight, Array.Empty<FloorPlanSave.FloorPlanItem>(), Flat);
        Assert.True(decision.Accepted);
        Assert.Equal(3, decision.DoorX);
        Assert.Equal(4, decision.DoorY);
        Assert.Equal(7, decision.WallHeight);
        Assert.Equal(FloorPlanSave.ErrorEntryOutside, Save("00\r00", doorX: -1, doorY: -1).Error);
    }

    [Fact]
    public void OptionalSaveFieldsParseTheSixAndSevenIntShapes()
    {
        var existing = new FloorPlanSave.Layout(1, 1, 1, 0, 0, 9);
        var six = FloorPlanRequest.Read(Packet("00\r00", new[] { 2, 0, 4, -1, 0 }));
        var sixLayout = FloorPlanSave.Resolve(six.DoorFieldsPresent, six.WallHeightPresent, six.Requested, existing);
        Assert.Equal(new FloorPlanSave.Layout(2, 0, 4, -1, 0, -1), sixLayout);

        var seven = FloorPlanRequest.Read(Packet("00\r00", new[] { 2, 0, 4, -1, 0, 6 }));
        var sevenLayout = FloorPlanSave.Resolve(seven.DoorFieldsPresent, seven.WallHeightPresent, seven.Requested, existing);
        Assert.Equal(new FloorPlanSave.Layout(2, 0, 4, -1, 0, 6), sevenLayout);

        var trailingBool = FloorPlanRequest.Read(Packet("00\r00", new[] { 2, 0, 4, 0, 0 }, new byte[] { 1 }));
        Assert.True(trailingBool.DoorFieldsPresent);
        Assert.False(trailingBool.WallHeightPresent);
    }

    private static FlashIncomingPacket Packet(string map, int[]? fields = null, byte[]? extra = null)
    {
        fields ??= Array.Empty<int>();
        var mapBytes = Encoding.UTF8.GetBytes(map);
        var bytes = new byte[2 + mapBytes.Length + (fields.Length * 4) + (extra?.Length ?? 0)];
        bytes[0] = (byte)(mapBytes.Length >> 8);
        bytes[1] = (byte)mapBytes.Length;
        mapBytes.CopyTo(bytes.AsSpan(2));
        var offset = 2 + mapBytes.Length;

        foreach (var field in fields)
        {
            bytes[offset++] = (byte)(field >> 24);
            bytes[offset++] = (byte)(field >> 16);
            bytes[offset++] = (byte)(field >> 8);
            bytes[offset++] = (byte)field;
        }

        extra?.CopyTo(bytes.AsSpan(offset));

        return new FlashIncomingPacket { Buffer = bytes };
    }

    private static FloorPlanSave.Decision Save(
        string map,
        int doorX = 0,
        int doorY = 0,
        int doorDirection = 2,
        int wallThickness = -2,
        int floorThickness = 1,
        int wallHeight = 0,
        IReadOnlyList<FloorPlanSave.FloorPlanItem>? items = null,
        IReadOnlyDictionary<(int X, int Y), FloorPlanSave.FloorTile>? tiles = null)
    {
        return FloorPlanSave.Evaluate(map, doorX, doorY, doorDirection, wallThickness, floorThickness, wallHeight, items ?? Array.Empty<FloorPlanSave.FloorPlanItem>(), tiles ?? Flat);
    }

    private static IReadOnlyDictionary<(int X, int Y), FloorPlanSave.FloorTile> WithHeight(int x, int y, int height)
    {
        var tiles = new Dictionary<(int X, int Y), FloorPlanSave.FloorTile>(Flat)
        {
            [(x, y)] = new(height, true)
        };

        return tiles;
    }
}
