using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Rooms;
using Plus.Core.Settings;
using Xunit;

namespace Plus.Tests;

public sealed class GroupPresentationServiceTests
{
    [Fact]
    public async Task HandlersDecodePrimitiveRequestAndDelegate()
    {
        var presentation = new RecordingPresentationService();

        await new GetGroupMembersEvent(presentation).Parse(
            null!, HabbiconTestSupport.Incoming(7, -3, "Den", 2));
        await new GetBadgeEditorPartsEvent(presentation).Parse(
            null!, HabbiconTestSupport.Incoming());

        Assert.Equal(new GroupMembersRequest(7, -3, "Den", 2), presentation.Request);
        Assert.True(presentation.BadgeEditorShown);
    }

    [Fact]
    public void MissingGroupDoesNotPublish()
    {
        var (client, sent) = Client(1);
        var service = new GroupPresentationService(GroupSource(null), Cache(), Rooms(), Settings(), NoGroupInfo());

        service.ShowMembers(client, new GroupMembersRequest(99, 0, "", 0));

        Assert.Empty(sent);
    }

    [Fact]
    public void RolesFilteringAndRequestAuthorizationMatchLegacyBehavior()
    {
        var group = Group(new GroupMembershipSnapshot([3], [1, 2], [4]));
        var users = new Dictionary<int, CachedUser>
        {
            [1] = User(1, "Creator"),
            [2] = User(2, "Admin"),
            [3] = User(3, "Member"),
            [4] = User(4, "Pending")
        };
        var service = new GroupPresentationService(GroupSource(group), Cache(users), Rooms(), Settings(), NoGroupInfo());
        var (owner, ownerSent) = Client(1);

        service.ShowMembers(owner, new GroupMembersRequest(group.Id, 0, "", 0));

        var members = DecodeMembers(Assert.Single(ownerSent).Payload);
        Assert.Equal(new[] { 0, 1, 2 }, members.Roles);
        Assert.Equal(new[] { "Creator", "Admin", "Member" }, members.Names);

        ownerSent.Clear();
        service.ShowMembers(owner, new GroupMembersRequest(group.Id, 0, "pen", 2));
        var requests = DecodeMembers(Assert.Single(ownerSent).Payload);
        Assert.Equal(new[] { 3 }, requests.Roles);
        Assert.Equal(new[] { "Pending" }, requests.Names);
        Assert.Equal(2, requests.RequestType);

        var (member, memberSent) = Client(3);
        service.ShowMembers(member, new GroupMembersRequest(group.Id, 0, "ad", 2));
        var fallback = DecodeMembers(Assert.Single(memberSent).Payload);
        Assert.Equal(0, fallback.RequestType);
        Assert.Equal(new[] { "Admin" }, fallback.Names);
    }

    [Fact]
    public void PagingKeepsNegativeWirePageAndCannotOverflow()
    {
        var ids = Enumerable.Range(1, 20).ToImmutableArray();
        var group = Group(new GroupMembershipSnapshot(ids, [], []), creatorId: 99);
        var users = ids.ToDictionary(id => id, id => User(id, $"Member{id:D2}"));
        var service = new GroupPresentationService(GroupSource(group), Cache(users), Rooms(), Settings(), NoGroupInfo());
        var (client, sent) = Client(99);

        service.ShowMembers(client, new GroupMembersRequest(group.Id, -1, "", 0));
        var negative = DecodeMembers(Assert.Single(sent).Payload);
        Assert.Equal(-1, negative.Page);
        Assert.Equal(14, negative.Names.Count);
        Assert.Equal("Member01", negative.Names[0]);

        sent.Clear();
        service.ShowMembers(client, new GroupMembersRequest(group.Id, 1, "", 0));
        var second = DecodeMembers(Assert.Single(sent).Payload);
        Assert.Equal(6, second.Names.Count);
        Assert.Equal("Member15", second.Names[0]);

        sent.Clear();
        service.ShowMembers(client, new GroupMembersRequest(group.Id, int.MaxValue, "", 0));
        var overflowSafe = DecodeMembers(Assert.Single(sent).Payload);
        Assert.Empty(overflowSafe.Names);
        Assert.Equal(20, overflowSafe.Total);
        Assert.Equal(int.MaxValue, overflowSafe.Page);
    }

    [Fact]
    public void GroupMembersComposerWritesExactCapturedFieldsAfterSourceMutation()
    {
        var group = Group(new GroupMembershipSnapshot([3], [1], []));
        var user = User(1, "Creator");
        var presentation = new GroupMembersPresentation(
            group.Id, group.Name, group.RoomId, group.Badge, 1,
            [new GroupMemberPresentation(0, user.Id, user.Username, user.Look)],
            true, -2, 1, "Cre");
        var first = Compose(new GroupMembersComposer(presentation));

        group.Id = 99;
        group.Name = "Changed";
        group.RoomId = 100;
        group.Badge = "changed";
        var second = Compose(new GroupMembersComposer(presentation));

        Assert.Equal(first, second);
        Assert.Equal(new object[]
        {
            7, "Group", (uint)42, "b01014", 1, 1,
            0, 1, "Creator", "hd-1", "",
            true, 14, -2, 1, "Cre"
        }, second);
    }

    [Fact]
    public void BadgeEditorComposerFreezesPartsAndColoursBeforeComposition()
    {
        var bases = new List<GroupBadgeParts> { new(1, "base-a", "base-b") };
        var symbols = new List<GroupBadgeParts> { new(2, "symbol-a", "symbol-b") };
        var baseColours = new List<GroupColours> { new(3, "AA0000") };
        var symbolColours = new List<GroupColours> { new(4, "00AA00") };
        var backgrounds = new List<GroupColours> { new(5, "0000AA") };
        var groups = GroupSource(null, bases, symbols, baseColours, symbolColours, backgrounds);
        var service = new GroupPresentationService(groups, Cache(), Rooms(), Settings(), NoGroupInfo());
        var (client, sent) = Client(1);

        service.ShowBadgeEditor(client);
        var payload = Assert.Single(sent).Payload.ToArray();
        bases.Clear();
        symbols.Clear();
        baseColours.Clear();
        symbolColours.Clear();
        backgrounds.Clear();

        var presentation = new BadgeEditorPresentation(
            [new BadgePartPresentation(1, "base-a", "base-b")],
            [new BadgePartPresentation(2, "symbol-a", "symbol-b")],
            [new BadgeColourPresentation(3, "AA0000")],
            [new BadgeColourPresentation(4, "00AA00")],
            [new BadgeColourPresentation(5, "0000AA")]);
        var first = Compose(new BadgeEditorPartsComposer(presentation));
        var second = Compose(new BadgeEditorPartsComposer(presentation));

        Assert.Equal(first, second);
        Assert.Equal(new object[]
        {
            1, 1, "base-a", "base-b",
            1, 2, "symbol-a", "symbol-b",
            1, 3, "AA0000",
            1, 4, "00AA00",
            1, 5, "0000AA"
        }, second);
        Assert.NotEmpty(payload);
    }

    private static Group Group(GroupMembershipSnapshot membership, int creatorId = 1) => new(
        7, "Group", "Description", "b01014", 42, creatorId,
        DateTimeOffset.UnixEpoch, 0, 1, 2, 0, false, membership);

    private static CachedUser User(int id, string username) => new()
    {
        Id = id,
        Username = username,
        Look = "hd-1"
    };

    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent) Client(int id) =>
        HabbiconTestSupport.Client(new Habbo { Id = id, Username = $"User{id}" });

    private static ICacheManager Cache(IReadOnlyDictionary<int, CachedUser>? users = null) =>
        Proxy<ICacheManager>((method, args) => method == nameof(ICacheManager.GenerateUser)
            ? users?.GetValueOrDefault((int)args[0]!)
            : throw new NotSupportedException(method));

    private static IGroupManager GroupSource(
        Group? group,
        ICollection<GroupBadgeParts>? bases = null,
        ICollection<GroupBadgeParts>? symbols = null,
        ICollection<GroupColours>? baseColours = null,
        ICollection<GroupColours>? symbolColours = null,
        ICollection<GroupColours>? backgrounds = null) =>
        Proxy<IGroupManager>((method, args) => method switch
        {
            nameof(IGroupManager.TryGetGroup) => ReturnGroup(args, group),
            "get_BadgeBases" => bases ?? [],
            "get_BadgeSymbols" => symbols ?? [],
            "get_BadgeBaseColours" => baseColours ?? [],
            "get_BadgeSymbolColours" => symbolColours ?? [],
            "get_BadgeBackColours" => backgrounds ?? [],
            _ => throw new NotSupportedException(method)
        });

    private static bool ReturnGroup(object?[] args, Group? group)
    {
        args[1] = group;

        return group != null && (int)args[0]! == group.Id;
    }

    private static object[] Compose(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);

        return packet.Writes.ToArray();
    }

    private static MembersPayload DecodeMembers(byte[] payload)
    {
        var reader = new PacketReader(payload);
        reader.ReadInt();
        reader.ReadString();
        reader.ReadInt();
        reader.ReadString();
        var total = reader.ReadInt();
        var count = reader.ReadInt();
        var roles = new List<int>();
        var names = new List<string>();

        for (var index = 0; index < count; index++) {
            roles.Add(reader.ReadInt());
            reader.ReadInt();
            names.Add(reader.ReadString());
            reader.ReadString();
            reader.ReadString();
        }

        reader.ReadBool();
        reader.ReadInt();
        var page = reader.ReadInt();
        var requestType = reader.ReadInt();
        reader.ReadString();

        return new MembersPayload(total, page, requestType, roles, names);
    }

    private static IRoomDataLoader Rooms() => Proxy<IRoomDataLoader>((_, _) => throw new NotSupportedException());
    private static ISettingsManager Settings() => Proxy<ISettingsManager>((_, _) => throw new NotSupportedException());

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;

        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Call(targetMethod!.Name, args!);
    }

    private sealed record MembersPayload(
        int Total,
        int Page,
        int RequestType,
        List<int> Roles,
        List<string> Names);

    private static IGroupInfoSnapshotService NoGroupInfo() =>
        Proxy<IGroupInfoSnapshotService>((method, _) => throw new NotSupportedException(method));

    private sealed class RecordingPresentationService : IGroupPresentationService
    {
        public GroupMembersRequest? Request { get; private set; }
        public bool BadgeEditorShown { get; private set; }
        public void ShowMembers(GameClient session, GroupMembersRequest request) => Request = request;
        public void ShowBadgeEditor(GameClient session) => BadgeEditorShown = true;
        public void ShowCreationWindow(GameClient session) => throw new NotSupportedException();
        public void ShowInfo(GameClient session, int groupId, bool newWindow) => throw new NotSupportedException();
        public void ShowFurnitureSettings(GameClient session, uint itemId, int groupId) => throw new NotSupportedException();
        public void ShowCatalogFurnitureConfiguration(GameClient session) => throw new NotSupportedException();
    }

    private sealed class PacketReader(byte[] payload)
    {
        private int _offset;

        public int ReadInt()
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(_offset, 4));
            _offset += 4;

            return value;
        }

        public bool ReadBool() => payload[_offset++] != 0;

        public string ReadString()
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(_offset, 2));
            _offset += 2;
            var value = Encoding.UTF8.GetString(payload, _offset, length);
            _offset += length;

            return value;
        }
    }
}
