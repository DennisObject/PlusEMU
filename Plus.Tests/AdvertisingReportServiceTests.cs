using System.Reflection;
using Plus.Communication.Packets.Incoming.Help;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class AdvertisingReportServiceTests
{
    private static readonly DateTimeOffset Now = new(2042, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void CooldownOpensAtTheExactCapturedDeadline(int secondsFromNow, bool allowed)
    {
        var reporter = new Habbo { Id = 1, AdvertisingReportAvailableAt = Now.AddSeconds(secondsFromNow) };
        var target = new Habbo { Id = 2, HasSpoken = true };
        var (client, sent) = HabbiconTestSupport.Client(reporter);
        var (targetClient, _) = HabbiconTestSupport.Client(target);
        var reports = 0;
        var manager = Manager((method, _) => method switch
        {
            "GetClientByUserId" => targetClient,
            "DoAdvertisingReport" => RecordReport(),
            _ => throw new NotSupportedException(method)
        });
        var clock = new FixedClock();

        new AdvertisingReportService(manager, clock).Submit(client, target.Id);

        Assert.Equal(allowed ? 1 : 0, reports);
        Assert.Equal(allowed, target.AdvertisingReported);
        Assert.Equal(allowed ? Now.AddMinutes(5) : Now.AddSeconds(secondsFromNow), reporter.AdvertisingReportAvailableAt);
        Assert.Equal(1, clock.Reads);
        Assert.Single(sent);
        object? RecordReport()
        {
            reports++;

            return null;
        }
    }

    [Fact]
    public void UnlimitedReportsUseTheSameCapturedInstantAndAllowAlreadyReportedTargets()
    {
        var reporter = new Habbo
        {
            Id = 1,
            Access = UserAccess.Create([], [new(PermissionKeys.ChatReportUnlimited, false)])
        };
        var target = new Habbo { Id = 2, HasSpoken = true, AdvertisingReported = true };
        var (client, _) = HabbiconTestSupport.Client(reporter);
        var (targetClient, _) = HabbiconTestSupport.Client(target);
        var reports = 0;
        var manager = Manager((method, _) => method switch
        {
            "GetClientByUserId" => targetClient,
            "DoAdvertisingReport" => RecordReport(),
            _ => throw new NotSupportedException(method)
        });
        var clock = new FixedClock();
        var service = new AdvertisingReportService(manager, clock);

        service.Submit(client, target.Id);
        service.Submit(client, target.Id);

        Assert.Equal(2, reports);
        Assert.Equal(Now, reporter.AdvertisingReportAvailableAt);
        Assert.Equal(2, clock.Reads);
        object? RecordReport()
        {
            reports++;

            return null;
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SelfOrBlockedReportsCannotResolveTheTarget(bool self, bool blocked)
    {
        var reporter = new Habbo { Id = 1, AdvertisingReportedBlocked = blocked };
        var (client, sent) = HabbiconTestSupport.Client(reporter);
        var manager = Manager((method, _) => throw new InvalidOperationException(method));
        var clock = new FixedClock();

        new AdvertisingReportService(manager, clock).Submit(client, self ? 1 : 2);

        Assert.Equal(0, clock.Reads);
        Assert.Null(reporter.AdvertisingReportAvailableAt);
        Assert.Equal(blocked ? 1 : 0, sent.Count);
    }

    [Fact]
    public async Task HandlerDecodesAndDelegatesTheTargetId()
    {
        var service = new RecordingService();
        await new SubmitBullyReportEvent(service).Parse(null!, HabbiconTestSupport.Incoming(42));
        Assert.Equal(42, service.TargetId);
    }

    private static IGameClientManager Manager(Func<string, object?[], object?> invoke)
    {
        var manager = DispatchProxy.Create<IGameClientManager, ManagerProxy>();
        ((ManagerProxy)(object)manager).Call = invoke;

        return manager;
    }

    public class ManagerProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }

    private sealed class FixedClock : TimeProvider
    {
        public int Reads
        {
            get; private set;
        }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }

    private sealed class RecordingService : IAdvertisingReportService
    {
        public int TargetId
        {
            get; private set;
        }
        public void Submit(GameClient reporter, int targetId) => TargetId = targetId;
    }
}
