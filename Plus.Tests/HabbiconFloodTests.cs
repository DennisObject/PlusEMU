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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
