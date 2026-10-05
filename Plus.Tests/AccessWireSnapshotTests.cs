using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public class AccessWireSnapshotTests
{
    [Fact]
    public void RightsCaptureOneInstantAndSurviveExpiryAndReplacement()
    {
        var clock = new Clock();
        var role = new AccessRole(9, "staff", "Staff", 90, 7, "B9", true,
            [PermissionKeys.Ambassador, PermissionKeys.ClubAccess], new Dictionary<string, int>());
        var access = UserAccess.Create([new(role, clock.Now.AddSeconds(1))], clock: clock);
        var before = clock.Reads;
        var composer = new UserRightsComposer(UserRightsSnapshot.Capture(access));
        Assert.Equal(1, clock.Reads - before);
        var expected = new object[] { 2, 7, true, 9, "Staff", "B9", 2,
            PermissionKeys.Ambassador, 1, PermissionKeys.ClubAccess, 1 };
        Assert.Equal(expected, Write(composer));

        clock.Now = clock.Now.AddSeconds(1);
        access.ReplaceWith(UserAccess.Empty);
        var afterCapture = clock.Reads;
        Assert.Equal(expected, Write(composer));
        Assert.Equal(expected, Write(composer));
        Assert.Equal(afterCapture, clock.Reads);
        Assert.Equal(0, UserRightsSnapshot.Capture(access).ClubLevel);
    }

    [Fact]
    public void ModelGeometryAndScalarListsAreFrozenAtConstruction()
    {
        var model = new RoomModel("test", 0, 0, 0, 0, "00\r0", 2, 0, false);
        var models = new List<RoomModel> { model };
        var modelComposer = new CreatableRoomModelsComposer(CreatableRoomModelSnapshot.Capture(models));
        var styles = new List<int> { 2, 3 };
        var styleComposer = new AllowedChatStylesComposer(styles);
        var unseen = new List<int> { 7, 8 };
        var unseenComposer = new HabbiconUnseenComposer(unseen);
        var tiles = new List<HeightMapUpdateComposer.Tile> { new(1, 2, 3) };
        var tileComposer = new HeightMapUpdateComposer(tiles);
        var composers = new IServerPacket[] { modelComposer, styleComposer, unseenComposer, tileComposer };
        var expected = composers.Select(Write).ToArray();
        Assert.Equal(new object[] { 1, "test", 3, 2, 2, 2 }, expected[0]);
        Assert.Equal(new object[] { 2, 2, 3 }, expected[1]);
        Assert.Equal(new object[] { (byte)1, (byte)1, (byte)2, (short)3 }, expected[3]);

        model.Id = "changed";
        model.Heightmap = "x";
        model.MapSizeX = 99;
        model.MapSizeY = 99;
        model.RequiredClubLevel = 0;
        models.Clear();
        styles.Clear();
        unseen.Clear();
        tiles.Clear();
        for (var index = 0; index < composers.Length; index++)
        {
            Assert.Equal(expected[index], Write(composers[index]));
            Assert.Equal(expected[index], Write(composers[index]));
        }
    }

    [Fact]
    public void HeightMapCapturesEncodedRowsBeforeTheSourceChanges()
    {
        var heights = new short[2, 3] { { -1, 256, 512 }, { 7, 1024, 16384 } };
        var composer = new HeightMapComposer(heights);
        var expected = new object[] { 2, 6, (short)-1, (short)7, (short)256, (short)1024, (short)512, (short)16384 };
        Assert.Equal(expected, Write(composer));

        heights[0, 0] = 99;
        heights[1, 2] = 100;
        Assert.Equal(expected, Write(composer));
        Assert.Equal(expected, Write(composer));
        Assert.Equal(new object[] { 0, 0 }, Write(new HeightMapComposer(new short[0, 2])));
        Assert.Equal(new object[] { 2, 0 }, Write(new HeightMapComposer(new short[2, 0])));
    }

    [Fact]
    public void RoomNotificationCapturesFilteredValuesInTheirOriginalOrder()
    {
        var values = new Dictionary<string, string>
        {
            ["message"] = "hello", ["empty"] = " ", ["linkUrl"] = "/room/1"
        };
        var composer = new RoomNotificationComposer("notice", values);
        var expected = new object[] { "notice", 2, "message", "hello", "linkUrl", "/room/1" };
        Assert.Equal(expected, Write(composer));
        values["message"] = "changed";
        values["empty"] = "visible";
        values.Clear();
        Assert.Equal(expected, Write(composer));
        Assert.Equal(expected, Write(composer));
        Assert.Equal(new object[] { "notice", 0 }, Write(new RoomNotificationComposer("notice")));
        Assert.Equal(new object[] { "notice", 1, "message", "hello" },
            Write(new RoomNotificationComposer("notice", "message", "hello")));
        Assert.Equal(new object[] { "notice", 2, "title", "title", "message", "message" },
            Write(new RoomNotificationComposer("title", "message", "notice", "", "")));
    }

    private static object[] Write(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes.ToArray();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return Now; }
    }
}
