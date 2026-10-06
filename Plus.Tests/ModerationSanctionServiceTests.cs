using System.Reflection;
using Plus.Communication.Packets.Incoming.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ModerationSanctionServiceTests
{
    private static readonly DateTimeOffset Now = new(2040, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Fact]
    public async Task HandlerDecodesAllSevenFieldsAndDelegatesOnlyTypedValues()
    {
        var service = new RecordingSanctions();
        var (actor, _) = HabbiconTestSupport.Client(new Habbo());
        await new ModerationBanEvent(service).Parse(actor,
            HabbiconTestSupport.Incoming(17, "reason", 12, "junk one", "junk two", true, false));
        Assert.Equal(new ModerationBanRequest(17, "reason", 12, true, false), service.Request);
        Assert.Same(actor, service.Actor);
    }

    [Fact]
    public async Task OfflineCanonicalTargetCanBeBannedWithoutDereferencingAClient()
    {
        var target = Target();
        var manager = new RecordingModeration();
        var clock = new CountingClock(Now);
        await Service(null, target, manager, clock).Ban(Actor(), new(2, "offline", 4, true, true));

        var call = Assert.Single(manager.Calls);
        Assert.Equal(("moderator", 2, "target", "offline", (DateTimeOffset?)Now.AddHours(4), true, null),
            (call.ModeratorId, call.UserId, call.Username, call.Reason, call.ExpiresAt, call.IncludeAddress, call.MachineId));
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public async Task LiveTargetDisconnectsBeforeCoordinatorAndSuppliesMachineId()
    {
        var target = Target();
        var (targetClient, _) = HabbiconTestSupport.Client(target);
#pragma warning disable CS0618
        targetClient.MachineId = "device-2";
#pragma warning restore CS0618
        var disconnected = false;
        targetClient.DisconnectRequested = () => disconnected = true;
        var manager = new RecordingModeration { BeforeCall = () => Assert.True(disconnected) };

        await Service(targetClient, null, manager, new CountingClock(Now))
            .Ban(Actor(), new(2, "live", 2, false, true));

        Assert.True(disconnected);
        Assert.Equal("device-2", Assert.Single(manager.Calls).MachineId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task InvalidDurationsDoNotLookupCoordinateOrDisconnect(int hours)
    {
        var target = Target();
        var (targetClient, _) = HabbiconTestSupport.Client(target);
        var disconnected = false;
        targetClient.DisconnectRequested = () => disconnected = true;
        var manager = new RecordingModeration();
        var clock = new CountingClock(Now);

        await Service(targetClient, target, manager, clock).Ban(Actor(), new(2, "bad", hours, false, false));

        Assert.False(disconnected);
        Assert.Empty(manager.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task ModToolNearMaximumDateRejectsExtremeHoursWithoutSideEffects(int hours)
    {
        var target = Target();
        var (targetClient, _) = HabbiconTestSupport.Client(target);
        var disconnected = false;
        targetClient.DisconnectRequested = () => disconnected = true;
        var manager = new RecordingModeration();
        var clock = new CountingClock(DateTimeOffset.MaxValue.AddTicks(-1));

        await Service(targetClient, target, manager, clock).Ban(Actor(), new(2, "bad", hours, false, false));

        Assert.Equal(1, clock.Reads);
        Assert.False(disconnected);
        Assert.Empty(manager.Calls);
    }

    [Fact]
    public async Task MissingAndNonOutrankedTargetsNeverReachCoordinator()
    {
        var manager = new RecordingModeration();
        await Service(null, null, manager, new CountingClock(Now)).Ban(Actor(), new(2, "missing", 1, false, false));
        await Service(null, Target(90), manager, new CountingClock(Now)).Ban(Actor(), new(2, "higher", 1, false, false));
        Assert.Empty(manager.Calls);
    }

    [Fact]
    public async Task ExtraBanRightsAreRequiredBeforeTargetLookup()
    {
        var manager = new RecordingModeration();
        var actor = Actor(PermissionKeys.ModerationBanSoft);
        await Service(null, Target(), manager, new CountingClock(Now)).Ban(actor, new(2, "ip", 1, true, false));
        await Service(null, Target(), manager, new CountingClock(Now)).Ban(actor, new(2, "machine", 1, false, true));
        Assert.Empty(manager.Calls);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    [InlineData("1.7976931348623157E+308")]
    [InlineData("-1")]
    public async Task BanCommandRejectsMalformedOrOverflowingDurations(string duration)
    {
        var target = Target();
        var (targetClient, _) = HabbiconTestSupport.Client(target);
        target.Client = targetClient;
        var disconnected = false;
        targetClient.DisconnectRequested = () => disconnected = true;
        var manager = new RecordingModeration();

        await new Plus.HabboHotel.Rooms.Chat.Commands.Moderator.BanCommand(manager.Proxy, new CountingClock(Now))
            .Execute(Actor(), null!, target, [duration, "reason"]);

        Assert.False(disconnected);
        Assert.Empty(manager.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("perm")]
    [InlineData("0.0000001")]
    public async Task BanCommandNearMaximumDateRejectsDurationsWithoutThrowingOrSideEffects(string duration)
    {
        var target = Target();
        var (targetClient, _) = HabbiconTestSupport.Client(target);
        target.Client = targetClient;
        var disconnected = false;
        targetClient.DisconnectRequested = () => disconnected = true;
        var manager = new RecordingModeration();
        var clock = new CountingClock(DateTimeOffset.MaxValue.AddTicks(-1));

        await new Plus.HabboHotel.Rooms.Chat.Commands.Moderator.BanCommand(manager.Proxy, clock)
            .Execute(Actor(), null!, target, [duration, "reason"]);

        Assert.Equal(1, clock.Reads);
        Assert.False(disconnected);
        Assert.Empty(manager.Calls);
    }

    [Theory]
    [InlineData("2", 7200)]
    [InlineData("perm", 78892200)]
    public async Task BanCommandPreservesNormalAndPermanentDurations(string duration, int seconds)
    {
        var target = Target();
        var manager = new RecordingModeration();
        await new Plus.HabboHotel.Rooms.Chat.Commands.Moderator.BanCommand(manager.Proxy, new CountingClock(Now))
            .Execute(Actor(), null!, target, [duration, "reason"]);
        Assert.Equal(Now.AddSeconds(seconds), Assert.Single(manager.Calls).ExpiresAt);
    }

    private static ModerationSanctionService Service(GameClient? online, Habbo? canonical, RecordingModeration moderation, TimeProvider clock) =>
        new(Proxy<IGameClientManager>((method, _) => method == nameof(IGameClientManager.GetClientByUserId) ? online : throw new NotSupportedException(method)),
            Proxy<IModeratorUserLookup>((method, _) => method == nameof(IModeratorUserLookup.GetById) ? canonical : throw new NotSupportedException(method)),
            moderation.Proxy, clock);

    private static GameClient Actor(params string[] permissions)
    {
        var effective = permissions.Length == 0
            ? new[] { PermissionKeys.ModerationBanSoft, PermissionKeys.ModerationIpBan, PermissionKeys.ModerationMachineBan }
            : permissions;
        return HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "moderator", Access = EditorTestSupport.Access(effective, 90) }).Client;
    }

    private static Habbo Target(int rank = 10) => new() { Id = 2, Username = "target", Access = EditorTestSupport.Access([], rank) };

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args!);
    }

    private sealed class RecordingSanctions : IModerationSanctionService
    {
        public GameClient? Actor { get; private set; }
        public ModerationBanRequest? Request { get; private set; }
        public Task Ban(GameClient actor, ModerationBanRequest request) { Actor = actor; Request = request; return Task.CompletedTask; }
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
    }

    private sealed class RecordingModeration
    {
        public List<Call> Calls { get; } = [];
        public Action? BeforeCall { get; init; }
        public IModerationManager Proxy { get; }

        public RecordingModeration()
        {
            Proxy = ModerationSanctionServiceTests.Proxy<IModerationManager>((method, args) =>
            {
                if (method != nameof(IModerationManager.BanAccount)) throw new NotSupportedException(method);
                BeforeCall?.Invoke();
                Calls.Add(new((string)args[0]!, (int)args[1]!, (string)args[2]!, (string)args[3]!,
                    (DateTimeOffset?)args[4], (CancellationToken)args[5]!, (bool)args[6]!, (string?)args[7]));
                return Task.CompletedTask;
            });
        }

        public sealed record Call(string ModeratorId, int UserId, string Username, string Reason, DateTimeOffset? ExpiresAt,
            CancellationToken Deadline, bool IncludeAddress, string? MachineId);
    }
}
