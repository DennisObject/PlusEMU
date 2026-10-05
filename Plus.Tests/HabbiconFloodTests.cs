using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public class HabbiconFloodTests
{
    [Fact]
    public void RapidHabiconsAndTextShareThresholdAndCannotBypassCooldownWithIdleTime()
    {
        var clock = new ManualTimeProvider();
        var messenger = new HabboMessenger(new(), new(), new(), clock);
        var friend = new MessengerBuddy { Id = 2 };
        for (int i = 0; i < 10; i++) Assert.True(messenger.TrySendHabbicon(clock.GetUtcNow()));
        Assert.Null(messenger.SendMessage(friend, "eleventh"));
        Assert.False(messenger.TrySendHabbicon(clock.GetUtcNow()));
        Assert.Equal(MessageError.Flooding, messenger.SendMessage(friend, "blocked"));
        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.False(messenger.TrySendHabbicon(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(38));
        Assert.False(messenger.TrySendHabbicon(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(messenger.TrySendHabbicon(clock.GetUtcNow()));
        Assert.Null(messenger.SendMessage(friend, "after cooldown"));
    }

    [Fact]
    public void NormalSpacedMessagesAndTwentySecondPauseDoNotStartAFloodCooldown()
    {
        var clock = new ManualTimeProvider();
        var messenger = new HabboMessenger(new(), new(), new(), clock);
        for (int i = 0; i < 25; i++)
        {
            Assert.True(messenger.TrySendHabbicon(clock.GetUtcNow()));
            clock.Advance(TimeSpan.FromSeconds(6));
        }
        for (int i = 0; i < 9; i++) Assert.True(messenger.TrySendHabbicon(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(messenger.TrySendHabbicon(clock.GetUtcNow()));
    }

    [Fact]
    public void CooldownUsesExactUtcTicksAndOneClockReadPerTextSend()
    {
        var clock = new CountingClock { Now = new DateTimeOffset(2040, 1, 2, 12, 0, 0, TimeSpan.FromHours(5)) };
        var messenger = new HabboMessenger(new(), new(), new(), clock);
        var friend = new MessengerBuddy { Id = 2 };
        clock.Reads = 0;
        for (var i = 0; i < 11; i++) Assert.Null(messenger.SendMessage(friend, "allowed"));
        Assert.Equal(MessageError.Flooding, messenger.SendMessage(friend, "starts cooldown"));
        Assert.Equal(12, clock.Reads);

        var deadline = clock.Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(-7));
        clock.Now = deadline.AddTicks(-1);
        Assert.Equal(MessageError.Flooding, messenger.SendMessage(friend, "before"));
        clock.Now = deadline;
        Assert.Null(messenger.SendMessage(friend, "exact"));
        clock.Now = deadline.AddTicks(1);
        Assert.Null(messenger.SendMessage(friend, "after"));
        Assert.Equal(15, clock.Reads);
    }

    [Fact]
    public void FloodAtTheMaximumInstantDoesNotOverflowOrBypassTheCooldown()
    {
        var clock = new CountingClock { Now = DateTimeOffset.MaxValue };
        var messenger = new HabboMessenger(new(), new(), new(), clock);
        for (var i = 0; i < 11; i++) Assert.True(messenger.TrySendHabbicon(clock.Now));

        Assert.False(messenger.TrySendHabbicon(clock.Now));
        Assert.False(messenger.TrySendHabbicon(clock.Now));
        clock.Now = clock.Now.AddTicks(-1);
        Assert.Equal(MessageError.Flooding, messenger.SendMessage(new MessengerBuddy { Id = 2 }, "backwards"));
        Assert.Equal(2, clock.Reads);
    }

    private sealed class CountingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public int Reads { get; set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return Now; }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
