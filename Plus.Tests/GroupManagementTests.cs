using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

[Collection("Group purchase")]
public class GroupManagementTests : IDisposable
{
    private GroupRemovalService Removal(IGroupManager groups, IRoomManager rooms, ISettingsManager? settings = null) =>
        new(groups, rooms, settings ?? Proxy<ISettingsManager>((_, _) => "50"),
            Proxy<IGameClientManager>((method, args) => method == "GetClientByUserId"
                ? _clients.GetValueOrDefault((int)args[0]!) : throw new InvalidOperationException(method)),
            GroupInfo(), new GroupRemovalStore(_database), new AccountSessionGate());

    private readonly FieldInfo _gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly FieldInfo _databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _previousGame;
    private readonly object? _previousDatabase;
    private readonly RecordingDatabase _database = new();
    private readonly Dictionary<int, GameClient> _clients = new();

    public GroupManagementTests()
    {
        _previousGame = _gameField.GetValue(null);
        _previousDatabase = _databaseField.GetValue(null);
        _databaseField.SetValue(null, _database);
        var clients = Proxy<IGameClientManager>((method, args) =>
        {
            if (method == "GetClientByUserId") {
                return _clients.GetValueOrDefault((int)args[0]!);
            }

            throw new InvalidOperationException(method);
        });
        _gameField.SetValue(null, Proxy<IGame>((method, _) => method == "get_ClientManager" ? clients : throw new InvalidOperationException(method)));
    }

    [Fact]
    public void SettingsPacketIncludesHomeroomAndForum()
    {
        var group = NewGroup(hasForum: true);
        var packet = new HabbiconTestSupport.RecordingPacket();
        Assert.True(GroupManagementSnapshotService.TryParseBadge(group.Badge, out var pieces));
        new ManageGroupComposer(new(true, group.RoomId, "HQ", group.Id, group.Name, group.Description,
            group.Colour1, group.Colour2, 0, group.AdminOnlyDeco, pieces, group.Badge, group.MemberCount, group.ForumEnabled)).Compose(packet);

        var reader = new ValueReader(packet.Writes);
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(42, reader.ReadInt());
        Assert.Equal("HQ", reader.ReadString());
        reader.ReadBool();
        reader.ReadBool();
        Assert.Equal(group.Id, reader.ReadInt());
        Assert.Equal("Crew", reader.ReadString());
        Assert.Equal("desc", reader.ReadString());
        Assert.Equal(42, reader.ReadInt());
        Assert.Equal(3, reader.ReadInt());
        Assert.Equal(4, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        reader.ReadBool();
        reader.ReadString();
        var parts = reader.ReadInt();

        for (var i = 0; i < parts; i++) {
            reader.ReadInt();
            reader.ReadInt();
            reader.ReadInt();
        }

        Assert.Equal(group.Badge, reader.ReadString());
        Assert.Equal(group.MemberCount, reader.ReadInt());
        Assert.True(reader.ReadBool());
        Assert.True(reader.End);
    }

    [Fact]
    public void ConstructedGroupKeepsForumFlag()
    {
        var group = NewGroup(hasForum: true);
        Assert.True(group.HasForum);
        Assert.True(group.ForumEnabled);
    }

    [Fact]
    public async Task BadgeSaveReadsFlatValuesAsTriplets()
    {
        var group = NewGroup(hasForum: false);
        group.Badge = "b05114s06114";
        var (client, sent) = Client(Owner());
        await new UpdateGroupBadgeEvent(Appearance(group)).Parse(client, Packet(group.Id, 6, 1, 1, 4, 2, 2, 4));

        Assert.Equal("b01014s02024", group.Badge);
        Assert.Contains("UPDATE `groups` SET `badge`", string.Join("\n", _database.Statements));
        Assert.Contains(ServerPacketHeader.GroupInfoComposer, sent.Select(item => item.Header));

        group.Badge = "b05114s06114";
        var written = _database.Statements.Count;
        await new UpdateGroupBadgeEvent(Appearance(group)).Parse(client, Packet(group.Id, 4, 1, 1, 4));
        Assert.Equal("b05114s06114", group.Badge);
        Assert.Equal(written, _database.Statements.Count);

        var (member, memberSent) = Client(new Habbo { Id = 2, Username = "Member2", Access = Rights() });
        await new UpdateGroupBadgeEvent(Appearance(group)).Parse(member, Packet(group.Id, 6, 1, 1, 4, 2, 2, 4));
        Assert.Equal("b05114s06114", group.Badge);
        Assert.Empty(memberSent);
    }

    [Fact]
    public async Task PreferencesSaveForumAndAckWhenRoomIsUnloaded()
    {
        var group = NewGroup(hasForum: false);
        var (client, sent) = Client(Owner());
        var rooms = Proxy<IRoomManager>((method, args) =>
        {
            Assert.Equal("TryGetRoom", method);
            args[1] = null;

            return false;
        });
        var settings = new GroupSettingsService(GroupSource(group), rooms, GroupInfo(), new GroupSettingsStore(_database));
        await new UpdateGroupSettingsEvent(settings).Parse(client, Packet(group.Id, 1, 0, true));

        Assert.Equal(GroupType.Locked, group.Type);
        Assert.Equal(0, group.AdminOnlyDeco);
        Assert.True(group.ForumEnabled);
        Assert.True(group.HasForum);
        Assert.Contains("forum_enabled", string.Join("\n", _database.Statements));
        var headers = sent.Select(item => item.Header).ToList();
        var info = headers.IndexOf(ServerPacketHeader.GroupInfoComposer);
        Assert.True(info >= 0);
        Assert.DoesNotContain(ServerPacketHeader.ManageGroupComposer, headers);
    }

    [Fact]
    public async Task MemberPagesAreFourteenAndPendingStaysWithAdmins()
    {
        var group = NewGroup(hasForum: false);

        for (var id = 2; id <= 21; id++) {
            group.PublishJoin(id);
        }

        group.Type = GroupType.Locked;
        group.PublishJoin(30);
        var cache = Proxy<ICacheManager>((method, args) =>
        {
            var id = (int)args[0]!;

            return new CachedUser { Id = id, Username = id == 30 ? "Pending" : "Member" + id, Look = "hr-1" };
        });
        var handler = new GetGroupMembersEvent(new GroupPresentationService(GroupSource(group), cache, Proxy<IRoomDataLoader>((_, _) => throw new NotSupportedException()), Proxy<ISettingsManager>((_, _) => throw new NotSupportedException()), GroupInfo()));
        var (member, memberSent) = Client(new Habbo { Id = 2, Username = "Member2", Access = Rights() });
        await handler.Parse(member, Packet(group.Id, 0, "", 0));
        var firstPage = DecodeMembers(memberSent[0].Payload);
        Assert.Equal(14, firstPage.Count);
        Assert.Equal(20, firstPage.Total);
        Assert.Equal(14, firstPage.PageSize);
        Assert.Equal(0, firstPage.Page);
        Assert.DoesNotContain(firstPage.Names, name => name == "Pending");

        memberSent.Clear();
        await handler.Parse(member, Packet(group.Id, 0, "member3", 2));
        var hiddenPending = DecodeMembers(memberSent[0].Payload);
        Assert.Equal(0, hiddenPending.Level);
        Assert.DoesNotContain(hiddenPending.Names, name => name == "Pending");
        Assert.Contains(hiddenPending.Names, name => name == "Member3");

        memberSent.Clear();
        await handler.Parse(member, Packet(group.Id, 1, "", 0));
        var secondPage = DecodeMembers(memberSent[0].Payload);
        Assert.Equal(6, secondPage.Count);
        Assert.Equal(20, secondPage.Total);
        Assert.Equal(1, secondPage.Page);
        Assert.DoesNotContain(secondPage.Names, name => name == "Pending");

        var (owner, ownerSent) = Client(Owner());
        await handler.Parse(owner, Packet(group.Id, 0, "pen", 2));
        var pending = DecodeMembers(ownerSent[0].Payload);
        Assert.Equal(2, pending.Level);
        Assert.Equal(new[] { "Pending" }, pending.Names);
    }

    [Fact]
    public async Task DeleteSucceedsWhenHomeroomIsNotLoaded()
    {
        var group = NewGroup(hasForum: false);
        var deleted = new List<int>();
        var unloaded = false;
        var groups = Proxy<IGroupManager>((method, args) =>
        {
            if (method == "TryGetGroup") {
                args[1] = group;

                return true;
            }

            if (method == "DeleteGroup") {
                deleted.Add((int)args[0]!);

                return null;
            }

            throw new InvalidOperationException(method);
        });
        var rooms = Proxy<IRoomManager>((method, args) =>
        {
            if (method == "TryGetRoom") {
                args[1] = null;

                return false;
            }

            if (method == "UnloadRoom") {
                unloaded = true;

                return null;
            }

            throw new InvalidOperationException(method);
        });
        var settings = Proxy<ISettingsManager>((_, _) => "50");
        var (stranger, strangerSent) = Client(new Habbo { Id = 8, Username = "Stranger", Access = Rights() });
        var handler = new DeleteGroupEvent(Removal(groups, rooms, settings));
        await handler.Parse(stranger, Packet(group.Id));
        Assert.Empty(deleted);
        Assert.NotEmpty(strangerSent);

        var (owner, ownerSent) = Client(Owner());
        await handler.Parse(owner, Packet(group.Id));
        Assert.Equal(new[] { group.Id }, deleted);
        Assert.Contains("DELETE FROM `groups`", string.Join("\n", _database.Statements));
        Assert.False(unloaded);
        var deactivated = ownerSent.Single(item => item.Header == ServerPacketHeader.GroupDeactivatedComposer);
        Assert.Equal(group.Id, BinaryPrimitives.ReadInt32BigEndian(deactivated.Payload));
    }

    [Fact]
    public async Task AcceptedRequestIsReportedAsMember()
    {
        var group = NewGroup(hasForum: false);
        group.Type = GroupType.Locked;
        group.PublishJoin(8);
        var (target, _) = Client(new Habbo { Id = 8, Username = "Bob", Look = "hr-1", Access = Rights() });
        var (owner, sent) = Client(Owner());
        await new AcceptGroupMembershipEvent(Mutations(group, UnloadedRooms())).Parse(owner, Packet(group.Id, 8));

        Assert.True(group.IsMember(8));
        Assert.False(group.HasRequest(8));
        var body = sent.Single(item => item.Header == ServerPacketHeader.GroupMemberUpdatedComposer).Payload;
        Assert.Equal(group.Id, BinaryPrimitives.ReadInt32BigEndian(body));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(4)));
        Assert.Equal(8, target.GetHabbo().Id);
    }

    [Fact]
    public void OctaneRevisionSeparatesKickConfirmationFromRemoval()
    {
        foreach (var name in new[] { "1.6.6.json", "3.6.0.json", "OCTANE-3-6-0-FLOOR-20260909.json" }) {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRevisions(), name)));
            var incoming = json.RootElement.GetProperty("IncomingHeaders");
            var outgoing = json.RootElement.GetProperty("OutgoingHeaders");
            Assert.Equal(593u, incoming.GetProperty("RemoveGroupMemberEvent").GetUInt32());
            Assert.Equal(3593u, incoming.GetProperty("ConfirmRemoveGroupMemberEvent").GetUInt32());
            Assert.Equal(1876u, outgoing.GetProperty("GroupConfirmRemoveMemberComposer").GetUInt32());
            Assert.Equal(3129u, outgoing.GetProperty("GroupDeactivatedComposer").GetUInt32());
        }
    }

    [Fact]
    public async Task ConfirmCountsFurnitureWithoutRemovingTheMember()
    {
        var group = NewGroup(hasForum: false);
        group.PublishJoin(8);
        _database.Scalar = 4;
        var (owner, sent) = Client(Owner());
        await new ConfirmRemoveGroupMemberEvent(Removal(GroupSource(group), UnloadedRooms())).Parse(owner, Packet(group.Id, 8));

        Assert.True(group.IsMember(8));
        Assert.Contains("SELECT COUNT(*) FROM `items`", string.Join("\n", _database.Statements));
        var body = sent.Single(item => item.Header == ServerPacketHeader.GroupConfirmRemoveMemberComposer).Payload;
        Assert.Equal(8, BinaryPrimitives.ReadInt32BigEndian(body));
        Assert.Equal(4, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(4)));
    }

    [Fact]
    public async Task KickRefreshDoesNotDependOnTheMembersPage()
    {
        var group = NewGroup(hasForum: false);
        group.PublishJoin(8);
        var rooms = UnloadedRooms();
        var (owner, sent) = Client(Owner());
        await new RemoveGroupMemberEvent(Removal(GroupSource(group), rooms)).Parse(owner, Packet(group.Id, 8, true));

        Assert.False(group.IsMember(8));
        var body = sent.Single(item => item.Header == ServerPacketHeader.UnknownGroupComposer).Payload;
        Assert.Equal(group.Id, BinaryPrimitives.ReadInt32BigEndian(body));
        Assert.Equal(8, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(4)));
    }

    [Fact]
    public async Task KickClearsFavouriteAndHomeroomRights()
    {
        var group = NewGroup(hasForum: false);
        group.PublishJoin(8);
        group.MakeAdmin(8);
        var stats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, group.Id, "", 0);
        var targetHabbo = new Habbo { Id = 8, Username = "Target", Access = Rights(), HabboStats = stats };
        var (targetClient, targetSent) = Client(targetHabbo);
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, manager);
        var roomUser = new RoomUser(8, group.RoomId, 3, room, targetClient, TestChatEmotions.Unused, TestRewardProgress.Unused);
        roomUser.SetStatus("flatctrl 1", "");
        roomUser.SetStatus("flatctrl 3", "");
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        users.TryAdd(roomUser.VirtualId, roomUser);
        targetHabbo.CurrentRoom = room;
        var rooms = Proxy<IRoomManager>((method, args) =>
        {
            Assert.Equal("TryGetRoom", method);
            args[1] = room;

            return true;
        });
        var (owner, sent) = Client(Owner());
        await new RemoveGroupMemberEvent(Removal(GroupSource(group), rooms)).Parse(owner, Packet(group.Id, 8, true));

        Assert.False(group.IsAdmin(8));
        Assert.False(group.IsMember(8));
        Assert.Equal(0, stats.FavouriteGroupId);
        Assert.Contains("UPDATE `user_statistics` SET `groupid` = 0", string.Join("\n", _database.Statements));
        Assert.DoesNotContain("flatctrl 1", roomUser.Statusses.Keys);
        Assert.DoesNotContain("flatctrl 3", roomUser.Statusses.Keys);
        var targetHeaders = targetSent.Select(item => item.Header).ToList();
        Assert.Contains(ServerPacketHeader.YouAreControllerComposer, targetHeaders);
        var favouriteUpdate = targetSent.Single(item => item.Header == ServerPacketHeader.UpdateFavouriteGroupComposer).Payload;
        Assert.Equal(roomUser.VirtualId, BinaryPrimitives.ReadInt32BigEndian(favouriteUpdate));
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(favouriteUpdate.AsSpan(4)));
        Assert.Contains(ServerPacketHeader.RefreshFavouriteGroupComposer, targetHeaders);
        Assert.Contains(ServerPacketHeader.GroupInfoComposer, targetHeaders);
        var refresh = sent.Single(item => item.Header == ServerPacketHeader.UnknownGroupComposer).Payload;
        Assert.Equal(group.Id, BinaryPrimitives.ReadInt32BigEndian(refresh));
        Assert.Equal(8, BinaryPrimitives.ReadInt32BigEndian(refresh.AsSpan(4)));
    }

    [Fact]
    public async Task LegacyRemovalPayloadStillRemovesAMember()
    {
        var group = NewGroup(hasForum: false);
        group.PublishJoin(8);
        var rooms = UnloadedRooms();
        var (owner, sent) = Client(Owner());
        await new RemoveGroupMemberEvent(Removal(GroupSource(group), rooms)).Parse(owner, Packet(group.Id, 8));

        Assert.False(group.IsMember(8));
        Assert.Contains(ServerPacketHeader.UnknownGroupComposer, sent.Select(item => item.Header));
    }

    [Fact]
    public async Task ConfirmAndRemovalRefuseTheOwnerAndOtherAdmins()
    {
        var group = NewGroup(hasForum: false);
        group.PublishJoin(7);
        group.PublishJoin(4);
        group.PublishJoin(5);
        group.MakeAdmin(4);
        group.MakeAdmin(5);
        var rooms = UnloadedRooms();
        var groups = GroupSource(group);
        var (admin, adminSent) = Client(new Habbo { Id = 4, Username = "Admin", Access = Rights() });
        await new ConfirmRemoveGroupMemberEvent(Removal(groups, UnloadedRooms())).Parse(admin, Packet(group.Id, 7));
        await new ConfirmRemoveGroupMemberEvent(Removal(groups, UnloadedRooms())).Parse(admin, Packet(group.Id, 5));
        await new RemoveGroupMemberEvent(Removal(groups, rooms)).Parse(admin, Packet(group.Id, 7));
        await new RemoveGroupMemberEvent(Removal(groups, rooms)).Parse(admin, Packet(group.Id, 5));
        Assert.DoesNotContain(adminSent, item => item.Header == ServerPacketHeader.GroupConfirmRemoveMemberComposer || item.Header == ServerPacketHeader.UnknownGroupComposer);
        Assert.True(group.IsMember(7));
        Assert.True(group.IsAdmin(5));

        var (member, memberSent) = Client(new Habbo { Id = 8, Username = "Member", Access = Rights() });
        group.PublishJoin(8);
        await new ConfirmRemoveGroupMemberEvent(Removal(groups, UnloadedRooms())).Parse(member, Packet(group.Id, 4));
        Assert.Empty(memberSent);
        Assert.True(group.IsAdmin(4));
    }

    [Fact]
    public async Task AdminCanLeaveWhileTheHomeroomIsUnloaded()
    {
        var group = NewGroup(hasForum: false);
        group.PublishJoin(11);
        group.MakeAdmin(11);
        Client(Owner());
        var leaver = new Habbo
        {
            Id = 11,
            Username = "Leaver",
            Access = Rights(),
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
        };
        var (client, sent) = Client(leaver);
        var rooms = Proxy<IRoomManager>((method, args) =>
        {
            Assert.Equal("TryGetRoom", method);
            args[1] = null;

            return false;
        });
        await new RemoveGroupMemberEvent(Removal(GroupSource(group), rooms)).Parse(client, Packet(group.Id, 11));

        Assert.False(group.IsMember(11));
        Assert.False(group.IsAdmin(11));
        Assert.Contains("DELETE FROM `group_memberships`", string.Join("\n", _database.Statements));
        Assert.Contains(ServerPacketHeader.GroupInfoComposer, sent.Select(item => item.Header));
    }

    [Fact]
    public async Task OwnerCanManageOfflineMembersAndApplicants()
    {
        var group = NewGroup(hasForum: false);
        group.Type = GroupType.Locked;
        group.PublishJoin(8);
        var (owner, sent) = Client(Owner());
        var rooms = UnloadedRooms();
        var mutations = Mutations(group, rooms, identities: false);
        await new AcceptGroupMembershipEvent(mutations).Parse(owner, Packet(group.Id, 8));
        Assert.True(group.IsMember(8));
        Assert.False(group.HasRequest(8));
        await new GiveAdminRightsEvent(mutations).Parse(owner, Packet(group.Id, 8));
        Assert.True(group.IsAdmin(8));
        await new TakeAdminRightsEvent(mutations).Parse(owner, Packet(group.Id, 8));
        Assert.False(group.IsAdmin(8));
        Assert.Equal(3, sent.Count(item => item.Header == ServerPacketHeader.UnknownGroupComposer));
    }

    private static IRoomManager UnloadedRooms() => Proxy<IRoomManager>((method, args) =>
    {
        Assert.Equal("TryGetRoom", method);
        args[1] = null;

        return false;
    });

    private Group NewGroup(bool hasForum)
    {
        var group = new Group(9, "Crew", "desc", "b01014s02024", 42, 7,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), 0, 3, 4, 0,
            hasForum, GroupMembershipSnapshot.Empty);

        return group;
    }

    private Habbo Owner() => new() { Id = 7, Username = "Owner", Access = Rights() };

    private static UserAccess Rights(params string[] rights) => EditorTestSupport.Access(rights);

    private (GameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(Habbo habbo)
    {
        var result = HabbiconTestSupport.Client(habbo);
        habbo.Access ??= Rights();
        _clients[habbo.Id] = result.Client;

        return result;
    }

    private static IGroupManager GroupSource(Group group) => Proxy<IGroupManager>((method, args) =>
    {
        if (method != "TryGetGroup") {
            throw new InvalidOperationException(method);
        }

        args[1] = group;

        return true;
    });

    private IGroupMembershipMutationService Mutations(Group group, IRoomManager rooms, bool identities = true) =>
        new GroupMembershipMutationService(
            GroupSource(group),
            rooms,
            Proxy<IGroupMemberIdentityLookup>((method, args) => method == nameof(IGroupMemberIdentityLookup.Find) && identities && _clients.TryGetValue((int)args[0]!, out var client)
                ? new GroupMemberIdentity(client.GetHabbo().Id, client.GetHabbo().Username, client.GetHabbo().Look)
                : null),
            new SuccessfulMutationStore());

    private IGroupInfoSnapshotService GroupInfo()
    {
        var clients = Proxy<IGameClientManager>((method, args) =>
            method == "GetClientByUserId" ? _clients.GetValueOrDefault((int)args[0]!) : throw new InvalidOperationException(method));
        var cache = Proxy<ICacheManager>((method, _) => method == "GenerateUser" ? null : throw new InvalidOperationException(method));

        return new GroupInfoSnapshotService(clients, cache, _database,
            Proxy<IRoomDataLoader>((method, args) =>
            {
                Assert.Equal(nameof(IRoomDataLoader.TryGetData), method);
                var data = (RoomData)RuntimeHelpers.GetUninitializedObject(typeof(RoomData));
                data.Id = (uint)args[0]!;
                data.Name = "HQ";
                args[1] = data;

                return true;
            }));
    }

    private IGroupAppearanceService Appearance(Group group) => new GroupAppearanceService(
        GroupSource(group),
        Proxy<IWordFilterManager>((method, args) => method == nameof(IWordFilterManager.CheckMessage) ? args[0] : throw new InvalidOperationException(method)),
        GroupInfo(),
        new GroupAppearanceStore(_database));

    private static MembersPage DecodeMembers(byte[] body)
    {
        var reader = new BodyReader(body);
        reader.ReadInt();
        reader.ReadString();
        reader.ReadInt();
        reader.ReadString();
        var total = reader.ReadInt();
        var count = reader.ReadInt();
        var names = new List<string>();

        for (var i = 0; i < count; i++) {
            reader.ReadInt();
            reader.ReadInt();
            names.Add(reader.ReadString());
            reader.ReadString();
            reader.ReadString();
        }

        reader.ReadBool();
        var pageSize = reader.ReadInt();
        var page = reader.ReadInt();
        var level = reader.ReadInt();

        return new MembersPage(total, count, pageSize, page, level, names);
    }

    private static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();

        foreach (var value in values) {
            if (value is int number) {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                stream.Write(bytes);
            }
            else if (value is bool flag) {
                stream.WriteByte(flag ? (byte)1 : (byte)0);
            }
            else if (value is string text) {
                var raw = Encoding.UTF8.GetBytes(text);
                var length = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)raw.Length);
                stream.Write(length);
                stream.Write(raw);
            }
            else {
                throw new InvalidOperationException(value.GetType().Name);
            }
        }

        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private static string RepoRevisions()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Plus Emulator.csproj"))) {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "Resources", "Revisions");
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;

        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }

    private sealed record MembersPage(int Total, int Count, int PageSize, int Page, int Level, List<string> Names);

    private sealed class SuccessfulMutationStore : IGroupMembershipMutationStore
    {
        public bool Accept(int groupId, int userId) => true;
        public bool Decline(int groupId, int userId) => true;
        public bool SetAdmin(int groupId, int userId, bool isAdmin) => true;
    }

    private sealed class ValueReader(List<object> values)
    {
        private int _index;
        public bool End => _index == values.Count;
        public int ReadInt() => (int)values[_index++];
        public string ReadString() => (string)values[_index++];
        public bool ReadBool() => (bool)values[_index++];
    }

    private sealed class BodyReader(byte[] body)
    {
        private int _offset;
        public int ReadInt()
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(_offset));
            _offset += 4;

            return value;
        }
        public bool ReadBool() => body[_offset++] == 1;
        public string ReadString()
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(_offset));
            _offset += 2;
            var value = Encoding.UTF8.GetString(body, _offset, length);
            _offset += length;

            return value;
        }
    }

    internal sealed class RecordingDatabase : IDatabase
    {
        public List<string> Statements { get; } = new();
        public int Scalar { get; set; }
        public string? Username { get; set; }
        public DataTable? OfferRows { get; set; }
        public List<string> Transactions { get; } = new();
        public List<(string Sql, Dictionary<string, object?> Parameters)> Writes { get; } = new();
        public bool FailInsert { get; set; }
        public List<(string Sql, Dictionary<string, object?> Parameters)> OfferQueries { get; } = new();
        public bool IsConnected() => true;
        public IDbConnection Connection() => new RecordingConnection(this);


    }

    private sealed class RecordingConnection(RecordingDatabase database) : IDbConnection
    {
        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 1;
        public string Database => "";
        public ConnectionState State { get; private set; } = ConnectionState.Open;
        public IDbTransaction BeginTransaction() => new RecordingTransaction(this, database);
        public IDbTransaction BeginTransaction(IsolationLevel il) => new RecordingTransaction(this, database, il);
        public void ChangeDatabase(string databaseName)
        {
        }
        public void Close() => State = ConnectionState.Closed;
        public IDbCommand CreateCommand() => new RecordingCommand(database);
        public void Open() => State = ConnectionState.Open;
        public void Dispose()
        {
        }
    }

    private sealed class RecordingTransaction(IDbConnection connection, RecordingDatabase database, IsolationLevel isolationLevel = IsolationLevel.Unspecified) : IDbTransaction
    {
        public IDbConnection Connection { get; } = connection;
        public IsolationLevel IsolationLevel { get; } = isolationLevel;
        public void Commit() => database.Transactions.Add("commit");
        public void Rollback() => database.Transactions.Add("rollback");
        public void Dispose() => database.Transactions.Add("dispose");
    }

    private sealed class RecordingCommand(RecordingDatabase database) : IDbCommand
    {
        public string CommandText { get; set; } = "";
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; } = CommandType.Text;
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters { get; } = new RecordingParameters();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel()
        {
        }
        public IDbDataParameter CreateParameter() => new RecordingParameter();
        public void Dispose()
        {
        }
        public int ExecuteNonQuery()
        {
            database.Statements.Add(CommandText);
            database.Writes.Add((CommandText, Parameters.Cast<RecordingParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value)));

            if (database.FailInsert && CommandText.Contains("INSERT INTO `catalog_marketplace_offers`", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("forced insert failure");
            }

            // A claim delete removes one row per expanded id, as the database would for rows that exist.
            if (CommandText.StartsWith("DELETE FROM `catalog_marketplace_offers`", StringComparison.OrdinalIgnoreCase)) {
                return Parameters.Cast<RecordingParameter>().Count(parameter => parameter.ParameterName.Contains("offerIds", StringComparison.Ordinal));
            }

            return 1;
        }
        public IDataReader ExecuteReader() => ExecuteReader(CommandBehavior.Default);
        public IDataReader ExecuteReader(CommandBehavior behavior)
        {
            database.Statements.Add(CommandText);

            if (CommandText.Contains("FROM `catalog_marketplace_offers`", StringComparison.OrdinalIgnoreCase)) {
                database.OfferQueries.Add((CommandText, Parameters.Cast<RecordingParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value)));

                return (database.OfferRows ?? new DataTable()).CreateDataReader();
            }

            if (CommandText.Contains("FROM group_memberships", StringComparison.OrdinalIgnoreCase)) {
                return EmptyReader(("UserId", typeof(int)), ("Rank", typeof(int)));
            }

            if (CommandText.Contains("FROM group_requests", StringComparison.OrdinalIgnoreCase)) {
                return EmptyReader(("user_id", typeof(int)));
            }

            if (CommandText.Contains("SELECT username FROM users", StringComparison.OrdinalIgnoreCase)) {
                return database.Username is { } name ? SingleValueReader("username", name) : EmptyReader(("username", typeof(string)));
            }

            if (CommandText.Contains("INNER JOIN `rooms`", StringComparison.OrdinalIgnoreCase)) {
                return EmptyReader();
            }

            return new ScalarReader(database.Scalar);
        }
        public object? ExecuteScalar()
        {
            database.Statements.Add(CommandText);

            return database.Scalar;
        }
        public void Prepare()
        {
        }

        private static IDataReader SingleValueReader(string column, string value)
        {
            var table = new DataTable();
            table.Columns.Add(column, typeof(string));
            table.Rows.Add(value);

            return table.CreateDataReader();
        }

        private static IDataReader EmptyReader(params (string Name, Type Type)[] columns)
        {
            var table = new DataTable();

            foreach (var column in columns) {
                table.Columns.Add(column.Name, column.Type);
            }

            return table.CreateDataReader();
        }
    }

    private sealed class ScalarReader(int value) : IDataReader
    {
        private bool _consumed;
        public int FieldCount => 1;
        public object this[int i] => value;
        public object this[string name] => value;
        public bool Read()
        {
            if (_consumed) {
                return false;
            }

            _consumed = true;

            return true;
        }
        public object GetValue(int i) => value;
        public int GetInt32(int i) => value;
        public long GetInt64(int i) => value;
        public Type GetFieldType(int i) => typeof(int);
        public string GetDataTypeName(int i) => "int";
        public string GetName(int i) => "count";
        public int GetOrdinal(string name) => 0;
        public bool IsDBNull(int i) => false;
        public int GetValues(object[] values)
        {
            values[0] = value;

            return 1;
        }
        public bool NextResult() => false;
        public void Close()
        {
        }
        public void Dispose()
        {
        }
        public int Depth => 0;
        public bool IsClosed => false;
        public int RecordsAffected => 1;
        public DataTable GetSchemaTable() => throw new NotSupportedException();
        public bool GetBoolean(int i) => throw new NotSupportedException();
        public byte GetByte(int i) => throw new NotSupportedException();
        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public char GetChar(int i) => throw new NotSupportedException();
        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public DateTime GetDateTime(int i) => throw new NotSupportedException();
        public decimal GetDecimal(int i) => throw new NotSupportedException();
        public double GetDouble(int i) => throw new NotSupportedException();
        public float GetFloat(int i) => throw new NotSupportedException();
        public Guid GetGuid(int i) => throw new NotSupportedException();
        public short GetInt16(int i) => throw new NotSupportedException();
        public string GetString(int i) => throw new NotSupportedException();
    }

    private sealed class RecordingParameters : List<RecordingParameter>, IDataParameterCollection
    {
        public object? this[string parameterName]
        {
            get => Find(parameter => parameter.ParameterName == parameterName);
            set => throw new NotSupportedException();
        }
        public bool Contains(string parameterName) => this.Any(parameter => parameter.ParameterName == parameterName);
        public int IndexOf(string parameterName) => FindIndex(parameter => parameter.ParameterName == parameterName);
        public void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
    }

    private sealed class RecordingParameter : IDbDataParameter
    {
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; }
        public bool IsNullable => true;
        public string ParameterName { get; set; } = "";
        public string SourceColumn { get; set; } = "";
        public DataRowVersion SourceVersion { get; set; }
        public object? Value { get; set; }
    }

    public void Dispose()
    {
        _gameField.SetValue(null, _previousGame);
        _databaseField.SetValue(null, _previousDatabase);
    }
}
