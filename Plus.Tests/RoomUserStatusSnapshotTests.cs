using System.Globalization;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Tests.Performance;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomUserStatusSnapshotTests
{
    [Fact]
    public void StatusWireShapeIsInvariantAndFrozenBeforeComposition()
    {
        var user = new RoomUser(7, 42, 9, null!, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 3, Y = 4, Z = 1.25, RotHead = 2, RotBody = 6 };
        user.Statusses.Add("sit", "0.5");
        var source = new List<RoomUser> { user };
        var previousCulture = CultureInfo.CurrentCulture;

        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var data = RoomUserStatusSnapshot.Capture(source);
            var composer = new UserUpdateComposer(data);
            var first = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(first);
            Assert.Equal(new object[] { 1, 9, 3, 4, "1.25", 2, 6, 0, "/sit 0.5//" }, first.Writes);

            user.X = 99;
            user.Z = 99;
            user.RotBody = 0;
            user.Statusses.Clear();
            source.Clear();
            var second = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(second);
            Assert.Equal(first.Writes, second.Writes);
        }
        finally {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void EmptyStatusRetainsTheTwoSlashesAndEmptyListRetainsTheCount()
    {
        var user = new RoomUser(7, 42, 9, null!, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        Assert.Equal("//", Assert.Single(RoomUserStatusSnapshot.Capture([user])).Status);
        var packet = new HabbiconTestSupport.RecordingPacket();
        new UserUpdateComposer([]).Compose(packet);
        Assert.Equal(new object[] { 0 }, packet.Writes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedBotStatusesAreClearedWithOrWithoutAViewer(bool withViewer)
    {
        var fixture = RoomPerformanceFixture.Create(2, withViewer ? 1 : 0);

        foreach (var user in fixture.Users) {
            user.UpdateNeeded = false;
        }

        foreach (var bot in fixture.Bots) {
            bot.UpdateNeeded = true;
        }

        var packets = new List<byte[]>();

        if (withViewer) {
            fixture.Clients[0].SendCallback = args =>
            {
                packets.Add(args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray());

                return true;
            };
        }

        fixture.Manager.SerializeStatusUpdates();

        Assert.All(fixture.Bots, bot => Assert.False(bot.UpdateNeeded));

        if (!withViewer) {
            Assert.Empty(packets);

            return;
        }

        var incoming = new FlashIncomingPacket { Buffer = Assert.Single(packets)[6..] };
        Assert.Equal(2, incoming.ReadInt());

        foreach (var bot in fixture.Bots) {
            Assert.Equal(bot.VirtualId, incoming.ReadInt());
            Assert.Equal(1, incoming.ReadInt());
            Assert.Equal(1, incoming.ReadInt());
            Assert.Equal("0", incoming.ReadString());
            Assert.Equal(bot.RotHead, incoming.ReadInt());
            Assert.Equal(bot.RotBody, incoming.ReadInt());
            Assert.Equal(0, incoming.ReadInt());
            Assert.Equal("/mv 2,1,0//", incoming.ReadString());
        }

        Assert.False(incoming.HasDataRemaining());
    }

    [Fact]
    public void FlashCodecConsumesNeutralJumpingPowerForZeroOneAndMultipleRecords()
    {
        var previousCulture = CultureInfo.CurrentCulture;

        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var standing = StatusUser(9, 3, 4, 0, 2, 6);
            var sit = StatusUser(10, 3, 4, 1.25, 2, 6, ("sit", "0.5"));
            var move = StatusUser(11, 1, 1, 0, 0, 3, ("mv", "2,1,0"));
            AssertFlash(RoomUserStatusSnapshot.Capture([]));
            AssertFlash(RoomUserStatusSnapshot.Capture([standing]));
            AssertFlash(RoomUserStatusSnapshot.Capture([sit]));
            AssertFlash(RoomUserStatusSnapshot.Capture([move]));
            AssertFlash(RoomUserStatusSnapshot.Capture([standing, sit, move]));
        }
        finally {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static RoomUser StatusUser(int virtualId, int x, int y, double z, int head, int body, params (string Key, string Value)[] statuses)
    {
        var user = new RoomUser(7, 42, virtualId, null!, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = x, Y = y, Z = z, RotHead = head, RotBody = body };

        foreach (var status in statuses) {
            user.Statusses.Add(status.Key, status.Value);
        }

        return user;
    }

    private static void AssertFlash(IReadOnlyList<RoomUserStatusSnapshot> users)
    {
        var incoming = FlashBody(new UserUpdateComposer([.. users]));
        Assert.Equal(users.Count, incoming.ReadInt());

        foreach (var user in users) {
            Assert.Equal(user.VirtualId, incoming.ReadInt());
            Assert.Equal(user.X, incoming.ReadInt());
            Assert.Equal(user.Y, incoming.ReadInt());
            Assert.Equal(user.Z, incoming.ReadString());
            Assert.Equal(user.HeadRotation, incoming.ReadInt());
            Assert.Equal(user.BodyRotation, incoming.ReadInt());
            Assert.Equal(0, incoming.ReadInt());
            Assert.Equal(user.Status, incoming.ReadString());
        }

        Assert.False(incoming.HasDataRemaining());
    }

    private static FlashIncomingPacket FlashBody(IServerPacket composer)
    {
        using var stream = PlusMemoryStream.GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));

        return new FlashIncomingPacket { Buffer = stream.ToArray().AsMemory(6) };
    }
}
