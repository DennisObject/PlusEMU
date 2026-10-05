using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Rooms.Action;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RoomMuteServiceTests
{
    private static readonly DateTimeOffset Now = new(2040, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task HandlerDecodesAllFieldsAndDelegatesPrimitiveRequest()
    {
        var service = new RecordingMuteService();
        await new MuteUserEvent(service).Parse(null!, HabbiconTestSupport.Incoming(8, 999, 12));
        Assert.Equal((8, 12), service.Request);
    }

    [Fact]
    public void CheckMuteIsUtcAndInactiveAtExactBoundaryInNonUtcZone()
    {
        var world = new World(new CountingClock(Now, TimeZoneInfo.CreateCustomTimeZone("mute-minus-seven", TimeSpan.FromHours(-7), "test", "test")));
        var deadline = Now.AddMilliseconds(500);
        world.Room.MutedUsers[world.Target.Id] = deadline;

        Assert.True(world.Room.CheckMute(world.TargetClient, deadline.AddTicks(-1)));
        Assert.True(world.Room.MutedUsers.ContainsKey(world.Target.Id));
        Assert.False(world.Room.CheckMute(world.TargetClient, deadline));
        Assert.False(world.Room.MutedUsers.ContainsKey(world.Target.Id));
        Assert.False(world.Room.CheckMute(world.TargetClient, deadline.AddTicks(1)));
    }

    [Fact]
    public void ServicePreservesAuthorizationActiveMuteAndOverflowContracts()
    {
        var clock = new CountingClock(Now, TimeZoneInfo.Utc);
        var world = new World(clock);
        var (achievements, achievementRecorder) = AchievementRecorder.Create();
        var service = new RoomMuteService(achievements, clock);

        service.Mute(world.OwnerClient, world.Target.Id, 2);
        Assert.Equal(Now.AddMinutes(2), world.Room.MutedUsers[world.Target.Id]);
        Assert.Equal(1, clock.Calls);
        Assert.Equal(1, achievementRecorder.Calls);
        Assert.Single(world.TargetPackets);

        clock.Now = Now.AddMinutes(1);
        service.Mute(world.OwnerClient, world.Target.Id, 5);
        Assert.Equal(Now.AddMinutes(2), world.Room.MutedUsers[world.Target.Id]);

        world.Room.MutedUsers.Clear();
        world.Room.OwnerName = "somebody-else";
        clock.Calls = 0;
        service.Mute(world.OwnerClient, world.Target.Id, 2);
        Assert.Empty(world.Room.MutedUsers);
        Assert.Equal(0, clock.Calls);

        world.Room.OwnerName = world.Owner.Username;
        service.Mute(world.OwnerClient, 404, 2);
        Assert.Empty(world.Room.MutedUsers);
        Assert.Equal(0, clock.Calls);

        world.Target.Access = EditorTestSupport.Access([], 10);
        service.Mute(world.OwnerClient, world.Target.Id, 2);
        Assert.Empty(world.Room.MutedUsers);
        Assert.Equal(0, clock.Calls);

        world.Target.Access = EditorTestSupport.Access([]);
        service.Mute(world.OwnerClient, world.Target.Id, int.MaxValue);
        Assert.Empty(world.Room.MutedUsers);
        Assert.Equal(1, clock.Calls);
    }

    [Fact]
    public void LegacyAndModernWiredMutesRefreshFromOneCapturedUtcInstant()
    {
        var clock = new CountingClock(Now, TimeZoneInfo.CreateCustomTimeZone("mute-plus-nine", TimeSpan.FromHours(9), "test", "test"));
        var world = new World(clock);
        var item = new Item { Id = 1, Definition = new() };
        var legacy = new MuteTriggererBox(world.Room, item, clock);
        legacy.HandleSave(HabbiconTestSupport.Incoming(0, 2, "legacy"));

        Assert.True(legacy.Execute(world.Target));
        Assert.Equal(Now.AddMinutes(2), world.Room.MutedUsers[world.Target.Id]);
        Assert.Equal(1, clock.Calls);
        clock.Now = Now.AddMinutes(1);
        Assert.True(legacy.Execute(world.Target));
        Assert.Equal(clock.Now.AddMinutes(2), world.Room.MutedUsers[world.Target.Id]);
        Assert.Equal(2, clock.Calls);

        clock.Now = Now.AddMinutes(5);
        var descriptor = WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_mute_triggerer");
        var modern = new WiredModernAction(world.Room, item, descriptor, new(), _ => { }, (_, _, _) => { }, new(), TestLogging.Logger, clock, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestWiredDefinitions.Unused);
        Assert.True(modern.TryValidateConfiguration(new() { IntParams = [3, 0] }, out var config, out var error), error);
        modern.ApplyConfiguration(config);
        var context = new WiredRuntimeContext(world.Room, new(WiredEventKind.Use) { Actor = world.TargetUser },
            new(() => Array.Empty<Item>(), () => new[] { world.TargetUser }), new NoWiredOperations());
        context.Triggering.UserIds.Add(world.TargetUser.VirtualId);

        Assert.True(modern.Execute(context));
        Assert.Equal(clock.Now.AddMinutes(3), world.Room.MutedUsers[world.Target.Id]);
        Assert.Equal(3, clock.Calls);
        Assert.True(world.Room.CheckMute(world.TargetClient, clock.Now.AddMinutes(3).AddTicks(-1)));
        Assert.False(world.Room.CheckMute(world.TargetClient, clock.Now.AddMinutes(3)));
    }

    private sealed class World
    {
        public Room Room { get; }
        public Habbo Owner { get; }
        public Habbo Target { get; }
        public GameClient OwnerClient { get; }
        public GameClient TargetClient { get; }
        public RoomUser TargetUser { get; }
        public List<(uint Header, byte[] Payload)> TargetPackets { get; }

        public World(TimeProvider clock)
        {
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            Room.MutedUsers = [];
            Room.UsersWithRights = [];
            Room.Type = "private";
            Room.OwnerName = "owner";
            Owner = new() { Id = 7, Username = "owner", CurrentRoom = Room, Access = EditorTestSupport.Access([], 10) };
            Target = new() { Id = 8, Username = "target", CurrentRoom = Room, Access = EditorTestSupport.Access([]) };
            (OwnerClient, _) = HabbiconTestSupport.Client(Owner);
            (TargetClient, TargetPackets) = HabbiconTestSupport.Client(Target);
            Owner.Client = OwnerClient;
            Target.Client = TargetClient;
            var users = new RoomUserManager(Room, TestRoomUserStore.Instance, clock, new TestRewardProgress());
            Set(Room, "_roomUserManager", users);
            Add(users, new RoomUser(Owner.Id, 1, 11, Room, OwnerClient));
            TargetUser = new RoomUser(Target.Id, 2, 12, Room, TargetClient);
            Add(users, TargetUser);
        }

        private static void Add(RoomUserManager manager, RoomUser user)
        {
            user.UserId = user.HabboId;
            ((ConcurrentDictionary<int, RoomUser>)Get(manager, "_users"))[user.VirtualId] = user;
        }
    }

    private sealed class RecordingMuteService : IRoomMuteService
    {
        public (int UserId, int Duration)? Request { get; private set; }
        public void Mute(GameClient session, int userId, int durationMinutes) => Request = (userId, durationMinutes);
    }

    private sealed class CountingClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public int Calls { get; set; }
        public override TimeZoneInfo LocalTimeZone => zone;
        public override DateTimeOffset GetUtcNow() { Calls++; return Now; }
    }

    public class AchievementRecorder : DispatchProxy
    {
        public int Calls { get; private set; }
        public static (IAchievementManager Service, AchievementRecorder Recorder) Create()
        {
            var service = DispatchProxy.Create<IAchievementManager, AchievementRecorder>();
            return (service, (AchievementRecorder)(object)service);
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAchievementManager.ProgressAchievement)) Calls++;
            return targetMethod?.ReturnType == typeof(bool) ? true : null;
        }
    }

    private sealed class NoWiredOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => false;
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => false;
        public void ResetTimers(IEnumerable<Item> targets) { }
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
