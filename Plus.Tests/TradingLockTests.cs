using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Moderation;
using Plus.Communication.Packets.Outgoing.Moderation;
using Xunit;

namespace Plus.Tests;

public class TradingLockTests
{
    [Fact]
    public void ModeratorInfoSerializesFutureDatesAndNullWithoutEpochCasts()
    {
        var now = new DateTimeOffset(2042, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var packet = new HabbiconTestSupport.RecordingPacket();
        var data = new ModerationUserData
        {
            Id = 42,
            Username = "target",
            Look = "figure",
            Mail = "user@example.com",
            AccountCreatedAt = now.AddDays(-1),
            LastOnlineAt = null,
            TradingLockExpiresAt = new DateTimeOffset(2042, 1, 3, 12, 0, 0, TimeSpan.FromHours(2)),
            TradingLockCount = 2
        };

        new ModeratorUserInfoComposer(data, true, now).Compose(packet);

        Assert.Equal(new object[] { 42, "target", "figure", 1440, 0, true, 0, 0, 0, 0, 2,
            "03/01/2042 10:00:00", "", 0, 0, "user@example.com", "" }, packet.Writes);
    }

    [Theory]
    [InlineData("-3", 1)]
    [InlineData("0.5", 1)]
    [InlineData("4.5", 4.5)]
    [InlineData("999", 365)]
    public async Task TradeBanClampsDurationBeforeCreatingUtcExpiry(string text, double expectedDays)
    {
        var store = new RecordingLocks();
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 90) };
        var target = new Habbo { Id = 2, Username = "target", Access = EditorTestSupport.Access([], 1) };
        var (session, _) = HabbiconTestSupport.Client(actor);

        await new TradeBanCommand(store).Execute(session, null!, target, [text]);

        Assert.Equal((target.Id, TimeSpan.FromDays(expectedDays)), store.Lock);
        Assert.Equal(store.Expiry, target.TradingLockExpiresAt);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task InvalidTradeBanInputDoesNotWrite(string text)
    {
        var store = new RecordingLocks();
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 90) };
        var target = new Habbo { Id = 2, Username = "target", Access = EditorTestSupport.Access([], 1) };
        var (session, _) = HabbiconTestSupport.Client(actor);

        await new TradeBanCommand(store).Execute(session, null!, target, [text]);

        Assert.Null(store.Lock);
        Assert.Null(store.Cleared);
    }

    [Fact]
    public async Task ZeroClearsTheExpiry()
    {
        var store = new RecordingLocks();
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 90) };
        var target = new Habbo { Id = 2, Username = "target", Access = EditorTestSupport.Access([], 1), TradingLockExpiresAt = store.Expiry };
        var (session, _) = HabbiconTestSupport.Client(actor);

        await new TradeBanCommand(store).Execute(session, null!, target, ["0"]);

        Assert.Equal(target.Id, store.Cleared);
        Assert.Null(target.TradingLockExpiresAt);
    }

    private sealed class RecordingLocks : ITradingLockService
    {
        public readonly DateTimeOffset Expiry = new(2042, 4, 1, 12, 0, 0, TimeSpan.Zero);
        public (int, TimeSpan)? Lock;
        public int? Cleared;
        public DateTimeOffset Set(int userId, TimeSpan duration)
        {
            Lock = (userId, duration);

            return Expiry;
        }
        public void Clear(int userId) => Cleared = userId;
        public bool IsLocked(Habbo habbo) => throw new NotSupportedException();
    }
}
