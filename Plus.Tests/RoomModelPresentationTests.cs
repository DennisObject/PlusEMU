using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomModelPresentationTests
{
    [Fact]
    public void ExistingConstructorCallersKeepTheLegacyPresentationDefaults()
    {
        var model = new RoomModel("legacy", 0, 0, 0, 2, "00\r00", 0, -1, false);
        Assert.Equal(RoomModelPresentation.Default, model.Presentation);
        Assert.Equal(32, model.Presentation.Scale);
        Assert.Equal(64, new RoomModelPresentation(false, 0, 3, 28.45f).Scale);
    }

    [WiredChestDatabaseFact]
    public void NativePresentationLoadsReloadsAndReachesActualRoomEntryPacket()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        CreateLegacyModels(fixture);
        ApplyMigration(fixture);
        fixture.Connection.Execute("INSERT INTO room_models(id,custom) VALUES('stock',FALSE),('custom',TRUE)");
        var cameraZ = BitConverter.Int32BitsToSingle(unchecked((int)0x41e3999a));
        fixture.Connection.Execute("UPDATE room_models SET half_scale=FALSE,camera_x=0,camera_y=3,camera_z=@cameraZ", new { cameraZ });
        var manager = Manager(fixture);
        manager.LoadModels();
        Assert.True(manager.LoadModel("custom"));

        foreach (var id in new[] { "stock", "custom" }) {
            Assert.True(manager.TryGetModel(id, out var model));
            AssertEntry(fixture, model, false, 0, 3, cameraZ);
        }

        fixture.Connection.Execute("UPDATE room_models SET camera_x=2,camera_y=4 WHERE id='custom'");
        manager.ReloadModel("custom");
        Assert.True(manager.TryGetModel("custom", out var reloaded));
        AssertEntry(fixture, reloaded, false, 2, 4, cameraZ);
    }

    [WiredChestDatabaseFact]
    public void RepeatMigrationPreservesProfilesAndOrdinaryFloorplanSaveDoesNotResetThem()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        CreateLegacyModels(fixture);
        fixture.Connection.Execute("INSERT INTO room_models(id,custom) VALUES('legacy',FALSE),('custom',TRUE)");
        ApplyMigration(fixture);
        var manager = Manager(fixture);
        manager.LoadModels();
        Assert.True(manager.TryGetModel("legacy", out var legacy));
        AssertEntry(fixture, legacy, true, 0, 0, 0);
        fixture.Connection.Execute("UPDATE room_models SET half_scale=FALSE,camera_x=-2,camera_y=3,camera_z=28.450000762939453125 WHERE id='custom'; ALTER TABLE rooms ADD model_name VARCHAR(100), ADD wallthick INT, ADD floorthick INT");
        ApplyMigration(fixture);
        var store = new FloorPlanStore(fixture.Database);
        var decision = FloorPlanSave.Evaluate("000\r000\r000", 1, 1, 2, 0, 0, 8, [], new Dictionary<(int X, int Y), FloorPlanSave.FloorTile>());
        Assert.True(decision.Accepted);
        store.Save(42, "custom", decision);
        Assert.True(manager.LoadModel("custom"));
        Assert.True(manager.TryGetModel("custom", out var custom));
        AssertEntry(fixture, custom, false, -2, 3, (float)28.450000762939453125);
        store.Save(42, "new_custom", decision);
        Assert.True(manager.LoadModel("new_custom"));
        Assert.True(manager.TryGetModel("new_custom", out var created));
        AssertEntry(fixture, created, true, 0, 0, 0);
    }

    [WiredChestDatabaseFact]
    public void FreshInstallSeedRowsUseDefaultsAndRemainCompatibleWithRepeatMigration()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var sql = File.ReadAllText(HabbiconPacketTests.Repo("Database/FreshInstall.sql"));
        var tableStart = sql.IndexOf("CREATE TABLE `room_models` (", StringComparison.Ordinal);
        var tableEnd = sql.IndexOf(";", tableStart, StringComparison.Ordinal);
        fixture.Connection.Execute(sql[tableStart..(tableEnd + 1)]);
        var seedStart = sql.IndexOf("INSERT INTO `room_models` ", StringComparison.Ordinal);
        var seedEnd = sql.IndexOf(";", seedStart, StringComparison.Ordinal);
        fixture.Connection.Execute(sql[seedStart..(seedEnd + 1)]);
        ApplyMigration(fixture);
        var count = fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM room_models");
        Assert.True(count > 0);
        Assert.Equal(count, fixture.Connection.QuerySingle<int>("SELECT COUNT(*) FROM room_models WHERE half_scale=TRUE AND camera_x=0 AND camera_y=0 AND camera_z=0"));
        var manager = Manager(fixture);
        manager.LoadModels();
        Assert.True(manager.TryGetModel("model_0", out var model));
        Assert.Equal(RoomModelPresentation.Default, model.Presentation);
    }

    private static void CreateLegacyModels(WiredChestDatabaseTests.Fixture fixture) => fixture.Connection.Execute("""
        CREATE TABLE room_models(id VARCHAR(100) PRIMARY KEY,door_x INT DEFAULT 0,door_y INT DEFAULT 0,
            door_z DOUBLE DEFAULT 0,door_dir INT DEFAULT 2,heightmap TEXT DEFAULT '00\r00',
            public_items TEXT DEFAULT '',required_club_level INT DEFAULT 0,required_permission VARCHAR(191),
            wall_height INT DEFAULT -1,custom BOOL DEFAULT FALSE);
        """);

    private static void ApplyMigration(WiredChestDatabaseTests.Fixture fixture) => fixture.Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/68_RoomModelPresentation.sql")));

    private static RoomManager Manager(WiredChestDatabaseTests.Fixture fixture) => new(NullLogger<RoomManager>.Instance, fixture.Database, null!, TimeProvider.System, new TestRoomFactory(), new TestRoomDataLoaderFactory());

    private static void AssertEntry(WiredChestDatabaseTests.Fixture fixture, RoomModel model, bool halfScale, int x, int y, float z)
    {
        var world = new WiredChestProtocolTests.World(fixture.Database);
        Set(world.Room, "_gamemap", new Gamemap(world.Room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        world.Room.GetGameMap().GenerateMaps();
        ((ConcurrentDictionary<int, RoomUser>)Get(world.Room.GetRoomUserManager(), "_users")).Clear();
        ((ConcurrentDictionary<uint, Item>)Get(world.Room.GetRoomItemHandler(), "_floorItems")).Clear();
        world.Room.SendObjects(world.Client);
        var payload = Assert.Single(world.Packets, packet => packet.Header == ServerPacketHeader.FloorHeightMapComposer).Payload;
        Assert.Equal(halfScale ? 1 : 0, payload[0]);
        Assert.Equal(model.WallHeight, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1)));
        var mapLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(5));
        var tail = payload.AsSpan(7 + mapLength);
        Assert.Equal(16, tail.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(tail));
        Assert.Equal(x, BinaryPrimitives.ReadInt32BigEndian(tail[4..]));
        Assert.Equal(y, BinaryPrimitives.ReadInt32BigEndian(tail[8..]));
        Assert.Equal(BitConverter.SingleToInt32Bits(z), BinaryPrimitives.ReadInt32BigEndian(tail[12..]));
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
