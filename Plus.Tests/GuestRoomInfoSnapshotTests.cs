using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Navigator;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class GuestRoomInfoSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2039, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, false, 56)]
    [InlineData(true, false, 58)]
    [InlineData(false, true, 60)]
    [InlineData(true, true, 62)]
    public void ComposerPreservesTheExactGuestRoomSequence(bool hasGroup, bool hasPromotion, int roomType)
    {
        var data = RoomData(hasGroup, hasPromotion);
        var snapshot = GuestRoomInfoService.Capture(data, new Habbo { Username = "owner" }, true, false, Now);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new GetGuestRoomResultComposer(snapshot).Compose(packet);

        var expected = new List<object>
        {
            true, 42u, "room", 7, "owner", 1, 3, 25, "description", 2, 91, 0, 12, 2, "one", "two", roomType
        };
        if (hasGroup)
            expected.AddRange([9, "group", "badge"]);
        if (hasPromotion)
            expected.AddRange(["promotion", "details", 2]);
        expected.AddRange([false, false, false, false, 1, 2, 3, true, 4, 5, 6, 7, 8]);
        Assert.Equal(expected, packet.Writes);
    }

    [Fact]
    public void SnapshotRecompositionIgnoresLaterRoomGroupPromotionAndTagMutation()
    {
        var data = RoomData(true, true);
        var snapshot = GuestRoomInfoService.Capture(data, new Habbo { Username = "owner" }, false, true, Now);
        var first = new HabbiconTestSupport.RecordingPacket();
        new GetGuestRoomResultComposer(snapshot).Compose(first);

        data.Name = "changed";
        data.Tags[0] = "changed";
        data.Group!.Name = "changed";
        data.Promotion!.Name = "changed";
        data.Promotion.Extend(TimeSpan.FromDays(1));
        var second = new HabbiconTestSupport.RecordingPacket();
        new GetGuestRoomResultComposer(snapshot).Compose(second);

        Assert.Equal(first.Writes, second.Writes);
        Assert.Contains("room", second.Writes);
        Assert.Contains("one", second.Writes);
        Assert.Contains("group", second.Writes);
        Assert.Contains("promotion", second.Writes);
        Assert.False((bool)second.Writes[0]);
        Assert.True((bool)second.Writes[^13]);
    }

    [Fact]
    public void CaptureSamplesPromotionTimeOnceAndPreservesOwnerOrModeratorPermission()
    {
        var data = RoomData(false, true);
        var clock = new CountingClock(Now);

        var visitor = GuestRoomInfoService.Capture(data, new Habbo { Username = "visitor" }, false, false, clock);
        var owner = GuestRoomInfoService.Capture(data, new Habbo { Username = "owner" }, false, false, clock);
        var moderator = GuestRoomInfoService.Capture(data, new Habbo
        {
            Username = "moderator",
            Access = EditorTestSupport.Access([PermissionKeys.ModerationTool])
        }, false, false, clock);

        Assert.Equal(3, clock.Reads);
        Assert.Equal(2, visitor.Promotion!.MinutesLeft);
        Assert.False(visitor.CanModify);
        Assert.True(owner.CanModify);
        Assert.True(moderator.CanModify);
    }

    [Fact]
    public async Task HandlerDecodesPrimitivesAndDelegatesWhileMissingRoomsSendNothing()
    {
        var service = new RecordingService { Result = Snapshot() };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "viewer" });
        await new GetGuestRoomEvent(service).Parse(client, HabbiconTestSupport.Incoming(42, 1, 0));

        Assert.Equal((42u, 7, true, false), service.LastRequest);
        Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.GetGuestRoomResultComposer, sent[0].Header);

        service.Result = null;
        await new GetGuestRoomEvent(service).Parse(client, HabbiconTestSupport.Incoming(43, 0, 1));
        Assert.Equal((43u, 7, false, true), service.LastRequest);
        Assert.Single(sent);
    }

    private static RoomData RoomData(bool hasGroup, bool hasPromotion)
    {
        var data = new RoomData
        {
            Id = 42,
            Name = "room",
            OwnerId = 7,
            OwnerName = "owner",
            Access = RoomAccess.Doorbell,
            UsersNow = 3,
            UsersMax = 25,
            Description = "description",
            TradeSettings = 2,
            Score = 91,
            Category = 12,
            Tags = ["one", "two"],
            WhoCanMute = 1,
            WhoCanKick = 2,
            WhoCanBan = 3,
            ChatMode = 4,
            ChatSize = 5,
            ChatSpeed = 6,
            ExtraFlood = 7,
            ChatDistance = 8
        };
        if (hasGroup)
        {
            var group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
            group.Id = 9;
            group.Name = "group";
            group.Badge = "badge";
            data.Group = group;
        }
        if (hasPromotion)
            data.Promotion = new("promotion", "details", Now, Now.AddSeconds(61), 1, TimeProvider.System);
        return data;
    }

    private static GuestRoomInfoSnapshot Snapshot() => new(false, 42, "room", 7, "owner", 0, 0, 25, "", 0, 0, 0,
        [], null, null, false, 0, 0, 0, false, 0, 0, 0, 0, 0);

    private sealed class RecordingService : IGuestRoomInfoService
    {
        public GuestRoomInfoSnapshot? Result { get; set; }
        public (uint RoomId, int UserId, bool IsLoading, bool CheckEntry) LastRequest { get; private set; }

        public GuestRoomInfoSnapshot? Capture(uint roomId, Habbo viewer, bool isLoading, bool checkEntry)
        {
            LastRequest = (roomId, viewer.Id, isLoading, checkEntry);
            return Result;
        }
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return now;
        }
    }
}
