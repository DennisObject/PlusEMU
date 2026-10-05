using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomSettingsSnapshotTests
{
    [Fact]
    public void SettingsSnapshotPreservesEveryWireFieldAndFreezesTagsAndModelCapacity()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        room.Name = "name";
        room.Description = "description";
        room.Access = RoomAccess.Password;
        room.Category = 36;
        room.UsersMax = 30;
        room.Model = new RoomModel("test", 0, 0, 0, 0, "00\r00", 0, 0, false);
        room.Model.MapSizeX = 11;
        room.Model.MapSizeY = 10;
        room.Tags.AddRange(["one", "two"]);
        room.TradeSettings = 2;
        room.AllowPets = true;
        room.AllowPetsEating = false;
        room.RoomBlockingEnabled = true;
        room.Hidewall = false;
        room.WallThickness = -1;
        room.FloorThickness = 1;
        room.ChatMode = 1;
        room.ChatSize = 2;
        room.ChatSpeed = 0;
        room.ChatDistance = 50;
        room.ExtraFlood = 2;
        room.WhoCanMute = 1;
        room.WhoCanKick = 2;
        room.WhoCanBan = 0;
        var composer = new RoomSettingsDataComposer(RoomSettingsSnapshot.Capture(room));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[]
        {
            42u, "name", "description", 2, 36, 30, 50, 2, "one", "two", 2, 1, 0, 1, 0,
            -1, 1, 1, 2, 0, 50, 2, true, 1, 2, 0
        }, before.Writes);

        room.Name = "changed";
        room.Tags.Clear();
        room.Model.MapSizeX = 1;
        room.WhoCanKick = 0;
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }
}
