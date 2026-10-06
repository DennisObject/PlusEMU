using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands.Administrator;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator.Fun;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ModeratorTargetHierarchyTests
{
    public static IEnumerable<object[]> Commands()
    {
        foreach (var command in new ITargetChatCommand[] {
            new DisconnectCommand(), new FlagUserCommand(null!), new MakeSayCommand(),
            new ForceSitCommand(), new UnFreezeCommand(null!), new AlertCommand(),
            new GiveCommand(), new GiveBadgeCommand(null!), new FreezeCommand(null!),
            new KickCommand(), new SummonCommand(null!), new MuteCommand(null!),
            new UnmuteCommand(null!), new TradeBanCommand(null!), new BanCommand(null!, TimeProvider.System),
            new IpBanCommand(null!, TimeProvider.System), new MipCommand(null!, TimeProvider.System) })
        {
            yield return [command, 50];
            yield return [command, 90];
        }
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task StaffCommandsRejectEqualOrHigherTargetsBeforeAnySideEffect(ITargetChatCommand command, int targetWeight)
    {
        var actor = new Habbo { Id = 1, Username = "actor", Access = EditorTestSupport.Access(["*"], 50) };
        var target = new Habbo { Id = 2, Username = "target", Access = EditorTestSupport.Access([], targetWeight), LastNameChangedAt = DateTimeOffset.FromUnixTimeSeconds(123) };
        var (session, _) = HabbiconTestSupport.Client(actor);
        var (targetSession, sent) = HabbiconTestSupport.Client(target);
        target.Client = targetSession;
        var disconnected = false;
        targetSession.DisconnectRequested = () => disconnected = true;

        // Missing room and null injected services ensure an unauthorized request stops before room or SQL work.
        await command.Execute(session, null!, target, ["target", "coins", "10"]);

        Assert.False(disconnected);
        Assert.False(target.ChangingName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(123), target.LastNameChangedAt);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task GivePreservesActorSelfTargetWhileKeepingCurrencyKeyChecks()
    {
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([PermissionKeys.CommandGiveCoins], 50) };
        var (session, sent) = HabbiconTestSupport.Client(actor);
        actor.Client = session;
        await new GiveCommand().Execute(session, null!, actor, ["actor", "coins", "10"]);
        Assert.Equal(10, actor.Credits);
        Assert.Single(sent);
    }

    [Fact]
    public async Task HigherWeightActorCanMakeStaffTargetSpeakEvenWhenTargetHasMakeSayPermission()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 90), CurrentRoom = room };
        var target = new Habbo { Id = 2, Access = EditorTestSupport.Access([PermissionKeys.ModerationMakeSayAny], 50), CurrentRoom = room };
        var (session, _) = HabbiconTestSupport.Client(actor);
        var (targetSession, sent) = HabbiconTestSupport.Client(target);
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        typeof(Room).GetField("_roomUserManager", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(room, manager);
        var user = new RoomUser(2, 1, 2, room, targetSession, TestChatEmotions.Unused, TestRewardProgress.Unused);
        ((ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!)[2] = user;
        await new MakeSayCommand().Execute(session, room, target, ["hello"]);
        Assert.Single(sent);
        Assert.Equal(Plus.Communication.Packets.Outgoing.ServerPacketHeader.ChatComposer, sent[0].Header);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoomMuteCommandsRejectAnOwnerTheActorDoesNotOutrank(bool muted)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.OwnerId = 2;
        room.RoomMuted = muted;
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 50), CurrentRoom = room };
        var (session, _) = HabbiconTestSupport.Client(actor);
        var access = DispatchProxy.Create<IAccessControl, DeniedHierarchy>();
        IChatCommand command = muted ? new RoomUnmuteCommand(access) : new RoomMuteCommand(access);
        command.Execute(session, room, ["reason"]);
        Assert.Equal(muted, room.RoomMuted);
        Assert.Equal((1, 2), ((DeniedHierarchy)access).Ids);
    }

    [Fact]
    public void DeleteGroupRejectsAnOwnerTheActorDoesNotOutrankBeforeSql()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
        group.CreatorId = 2;
        room.Group = group;
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 50), CurrentRoom = room };
        var (session, _) = HabbiconTestSupport.Client(actor);
        var access = DispatchProxy.Create<IAccessControl, DeniedHierarchy>();
        new DeleteGroupCommand(null!, null!, EditorTestSupport.UntouchableDatabase(), access).Execute(session, room, []);
        Assert.Same(group, room.Group);
        Assert.Equal((1, 2), ((DeniedHierarchy)access).Ids);
    }

    public class DeniedHierarchy : DispatchProxy
    {
        public (int, int)? Ids;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IAccessControl.Outranks)) throw new InvalidOperationException(method?.Name);
            Ids = ((int)args![0]!, (int)args[1]!);
            return false;
        }
    }

    [Fact]
    public async Task HigherWeightActorCanDisconnectStaffEvenWhenTargetHasDisconnectPermission()
    {
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 90) };
        var target = new Habbo { Id = 2, Access = EditorTestSupport.Access([PermissionKeys.ModerationTool, PermissionKeys.ModerationDisconnectAny], 50) };
        var (session, _) = HabbiconTestSupport.Client(actor);
        var (targetSession, _) = HabbiconTestSupport.Client(target);
        target.Client = targetSession;
        var disconnected = false;
        targetSession.DisconnectRequested = () => disconnected = true;
        await new DisconnectCommand().Execute(session, null!, target, []);
        Assert.True(disconnected);
    }

    [Fact]
    public async Task HigherWeightActorCanFlagStaffTarget()
    {
        var actor = new Habbo { Id = 1, Access = EditorTestSupport.Access([], 90) };
        var target = new Habbo { Id = 2, Username = "target", Access = EditorTestSupport.Access([PermissionKeys.ModerationTool], 50), LastNameChangedAt = DateTimeOffset.FromUnixTimeSeconds(123), HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0) };
        var (session, _) = HabbiconTestSupport.Client(actor);
        var (targetSession, sent) = HabbiconTestSupport.Client(target);
        target.Client = targetSession;
        var persistence = new ProfilePersistence();
        await new FlagUserCommand(persistence).Execute(session, null!, target, []);
        Assert.True(target.ChangingName);
        Assert.Null(target.LastNameChangedAt);
        Assert.Equal((target.Id, "last_change", null), persistence.Update);
        Assert.Equal(2, sent.Count);
    }
    private sealed class ProfilePersistence : IUserPersistenceService
    {
        public (int, string, object?)? Update;
        public void Save(Habbo habbo, bool reopenModerationTickets = false) => throw new NotSupportedException();
        public void SetProfileValue(int userId, string column, object? value) => Update = (userId, column, value);
    }
}
