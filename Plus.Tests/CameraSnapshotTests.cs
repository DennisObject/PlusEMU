using Plus.HabboHotel.Camera;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public class CameraSnapshotTests
{
    [Fact]
    public void CaptureCopiesRoomStateAndSkipsUrlFurniture()
    {
        var chair = Floor(1, 100, "2", 3, 4, 0.5, 2);
        var photo = Floor(2, 4597, "/camera/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa.png", 1, 1, 0, 0);
        photo.Definition.InteractionType = InteractionType.CameraPicture;
        var branded = Wall(3, 50, "state\t0\nimageUrl\thttp://cdn.example/ad.png", ":w=1,1 l=2,2");
        branded.Definition.InteractionType = InteractionType.Background;
        var linked = Floor(4, 12, "https://evil.example/tile.png", 0, 0, 0, 0);
        var poster = Wall(5, 77, "8", ":w=3,3 l=1,1 l");
        var brokenWall = Wall(6, 78, "1", ":w=3,3 l=1,1");
        var items = new List<Item> { chair, photo, branded, linked, poster, brokenWall };
        var speeches = new List<RandomSpeech>();
        var bot = new RoomBot(9, 4, "generic", "freeroam", "Ada", "", "hd-180-1.ch-255-66.lg-280-110", 1, 2, 0, 2,
            0, 0, 0, 0, ref speeches, "F", 0, 3, false, 0, false, 0);
        var sitting = new RoomUser(0, 4, 8, null!)
        {
            BotData = bot,
            X = 6,
            Y = 7,
            Z = 1.25,
            RotBody = 4,
            RotHead = 4,
            DanceId = 2,
            CarryItemId = 5
        };
        sitting.SetStatus("sit", "1.0");
        sitting.SetStatus("gst", "sml");
        var users = new List<RoomUser> { sitting, new RoomUser(5, 4, 1, null!) };

        var scene = CameraSnapshotBuilder.Compose(Shell("0\nxxx\r\n1"), items, users);
        chair.ExtraData = new LegacyDataFormat { Data = "9" };
        items.Clear();
        sitting.SetStatus("lay", "0");

        var kept = Assert.Single(scene.Items, item => item.Id == 1);
        Assert.Equal(100, kept.SpriteId);
        Assert.Equal("s", kept.Type);
        Assert.Equal(3, kept.X);
        Assert.Equal(4, kept.Y);
        Assert.Equal(0.5, kept.Z);
        Assert.Equal(2, kept.Direction);
        Assert.Equal(2, kept.State);
        Assert.Equal("2", kept.ExtraData);
        Assert.Equal("", kept.WallPosition);
        var wall = Assert.Single(scene.Items, item => item.Id == 5);
        Assert.Equal("i", wall.Type);
        Assert.Equal(8, wall.State);
        Assert.Equal(":w=3,3 l=1,1 l", wall.WallPosition);
        Assert.DoesNotContain(scene.Items, item => item.Id is 2 or 3 or 4 or 6);
        Assert.DoesNotContain(scene.Items, item => item.ExtraData.Contains("http", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scene.Items, item => item.ExtraData.Contains("/camera/", StringComparison.Ordinal));

        Assert.Equal("0\rxxx\r1", scene.Heightmap);
        Assert.Equal(4u, scene.RoomId);
        Assert.Equal(new CameraDoor(1, 2, 3, 6), scene.Door);
        Assert.Equal(-1, scene.WallHeight);
        Assert.Equal(1, scene.WallThickness);
        Assert.Equal(-2, scene.FloorThickness);
        Assert.True(scene.HideWalls);
        Assert.Equal("201", scene.Floor);
        Assert.Equal("101", scene.Wallpaper);
        Assert.Equal("", scene.Landscape);
        Assert.Equal(0, scene.BackgroundColor);

        var avatar = Assert.Single(scene.Users);
        Assert.Equal(9, avatar.Id);
        Assert.Equal(8, avatar.RoomIndex);
        Assert.Equal("hd-180-1.ch-255-66.lg-280-110", avatar.Figure);
        Assert.Equal("f", avatar.Gender);
        Assert.Equal(CameraSnapshotBuilder.AvatarRentableBot, avatar.Type);
        Assert.Equal(6, avatar.X);
        Assert.Equal(7, avatar.Y);
        Assert.Equal(1.25, avatar.Z);
        Assert.Equal(4, avatar.Direction);
        Assert.Equal(4, avatar.HeadDirection);
        Assert.Equal("sit", avatar.Posture);
        Assert.Equal("1.0", avatar.PostureParameter);
        Assert.Equal("sml", avatar.Gesture);
        Assert.Equal(5, avatar.HandItem);
        Assert.Equal(2, avatar.Dance);
    }

    [Fact]
    public void BoundsStopAtRendererCapsAndMotionsUseRoomStatus()
    {
        var items = Enumerable.Range(1, CameraSnapshotBuilder.MaxItems + 1)
            .Select(id => Floor((uint)id, id, "0", 0, 0, 0, 0))
            .ToArray();
        var users = Enumerable.Range(0, CameraSnapshotBuilder.MaxUsers + 1)
            .Select(index =>
            {
                var speeches = new List<RandomSpeech>();
                var bot = new RoomBot(index + 1, 4, "generic", "freeroam", "B", "", "hr-1", 0, 0, 0, 0,
                    0, 0, 0, 0, ref speeches, "M", 0, 1, false, 0, false, 0);
                return new RoomUser(0, 4, index, null!) { BotData = bot };
            })
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => CameraSnapshotBuilder.Compose(Shell("x"), items, []));
        Assert.Throws<InvalidOperationException>(() => CameraSnapshotBuilder.Compose(Shell("x"), [], users));
        Assert.Throws<InvalidOperationException>(() => CameraSnapshotBuilder.NormalizeHeightmap(new string('a', CameraSnapshotBuilder.MaxHeightmapChars + 1)));

        Assert.Equal(("lay", "2.0", ""), CameraSnapshotBuilder.Motions(new Dictionary<string, string> { ["lay"] = "2.0", ["sit"] = "1" }, false));
        Assert.Equal(("mv", "3,4,1.0", "wav"), CameraSnapshotBuilder.Motions(new Dictionary<string, string> { ["mv"] = "3,4,1.0", ["gst"] = "wav" }, false));
        Assert.Equal(("std", "", "1"), CameraSnapshotBuilder.Motions(new Dictionary<string, string> { ["sign"] = "1" }, false));
        Assert.Equal(1, CameraSnapshotBuilder.AvatarUser);
        Assert.Equal(2, CameraSnapshotBuilder.AvatarPet);
        Assert.Equal(4, CameraSnapshotBuilder.AvatarRentableBot);
    }

    private static CameraRoomShell Shell(string heightmap) =>
        new(4, heightmap, 1, 2, 3, 6, -1, 1, -2, true, "201", "101", "https://cdn.example/landscape.png");

    private static Item Floor(uint id, int sprite, string extra, int x, int y, double z, int rotation) =>
        new()
        {
            Id = id,
            Definition = new ItemDefinition { SpriteId = sprite, Type = ItemType.Floor, InteractionType = InteractionType.None },
            ExtraData = new LegacyDataFormat { Data = extra },
            GetX = x,
            GetY = y,
            GetZ = z,
            Rotation = rotation
        };

    private static Item Wall(uint id, int sprite, string extra, string position)
    {
        var item = Floor(id, sprite, extra, 0, 0, 0, 0);
        item.Definition.Type = ItemType.Wall;
        item.WallCoordinates = position;
        return item;
    }
}
