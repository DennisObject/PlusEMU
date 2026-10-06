using System.Reflection;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Rewards;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ScheduledRewardTests
{
    private static readonly DateTimeOffset Start = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(1);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void ActiveWindowIsInclusiveAndFutureSafe(int minutes, bool expected)
    {
        var reward = new Reward(Start, End, "credits", "1", "");
        Assert.Equal(expected, reward.IsActiveAt(Start.AddMinutes(minutes)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MissingBoundsAreInactive(bool missingStart, bool missingEnd)
    {
        var reward = new Reward(missingStart ? null : Start, missingEnd ? null : End, "credits", "1", "");
        Assert.False(reward.IsActiveAt(Start));
    }

    [Fact]
    public async Task ManagerSamplesClockOncePerCheckBatch()
    {
        var clock = new CountingTimeProvider(Start);
        var manager = new RewardManager(Proxy<IDatabase>(), Proxy<IBadgeManager>(), clock);
        var (client, _) = HabbiconTestSupport.Client(new Habbo());

        await manager.CheckRewards(client);

        Assert.Equal(1, clock.Reads);
    }

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, EmptyProxy>();
    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    }

    private sealed class CountingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public int Reads
        {
            get; private set;
        }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return now;
        }
    }
}
