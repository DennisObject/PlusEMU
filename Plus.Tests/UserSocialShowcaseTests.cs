using System.Collections.Immutable;
using System.Reflection;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Users;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Ignores;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public sealed class UserSocialShowcaseTests
{
    [Fact]
    public async Task HandlersOnlyDecodeAndDelegate()
    {
        var service = new RecordingShowcase();
        await new GetRelationshipsEvent(service).Parse(null!, HabbiconTestSupport.Incoming(42));
        await new GetIgnoredUsersEvent(service).Parse(null!, null!);
        await new GetHabboGroupBadgesEvent(service).Parse(null!, null!);
        Assert.Equal(42, service.UserId);
        Assert.Equal(1, service.IgnoredViews);
        Assert.Equal(1, service.GroupViews);
    }

    [Fact]
    public void RelationshipAndGroupPacketsKeepExactCapturedFieldsAfterSourceMutation()
    {
        var buddy = new MessengerBuddy { Id = 7, Username = "Alice", Look = "look" };
        var entries = new[] { new RelationshipEntry(2, 3, buddy.Id, buddy.Username, buddy.Look) };
        var composer = new GetRelationshipsComposer(new(42, entries.ToImmutableArray()));
        var first = Compose(composer);
        buddy.Id = 999;
        buddy.Username = "changed";
        entries[0] = new(1, 99, 99, "other", "other");
        Assert.Equal(new object[] { 42, 1, 2, 3, 7, "Alice", "look" }, first);
        Assert.Equal(first, Compose(composer));

        var groups = new[] { new GroupBadgeSnapshot(8, "badge") };
        var groupComposer = new HabboGroupBadgesComposer(groups.ToImmutableArray());
        var original = Compose(groupComposer);
        groups[0] = new(99, "changed");
        Assert.Equal(new object[] { 1, 8, "badge" }, original);
        Assert.Equal(original, Compose(groupComposer));
    }

    [Fact]
    public void IgnoredNamesAndNameSuggestionsCopySourceCollections()
    {
        var names = new List<string> { "Alice", "Bob" };
        var ignored = new IgnoredUsersComposer(names);
        var suggestions = new NameChangeUpdateComposer("Dennis", NameChangeError.InUse, names);
        var ignoredBefore = Compose(ignored);
        var suggestionsBefore = Compose(suggestions);
        names[0] = "changed";
        names.Clear();
        Assert.Equal(new object[] { 2, "Alice", "Bob" }, ignoredBefore);
        Assert.Equal(new object[] { (int)NameChangeError.InUse, "Dennis", 2, "DennisAlice", "DennisBob" }, suggestionsBefore);
        Assert.Equal(ignoredBefore, Compose(ignored));
        Assert.Equal(suggestionsBefore, Compose(suggestions));
    }

    [Fact]
    public async Task OnlineRelationshipsUseLiveFriendsAndOfflineRelationshipsUseLoader()
    {
        var (viewer, sent) = HabbiconTestSupport.Client(new Habbo { Id = 42 });
        var friends = new Dictionary<int, MessengerBuddy>
        {
            [7] = new() { Id = 7, Username = "Alice", Look = "look", Relationship = 2 },
            [8] = new() { Id = 8, Username = "Bob", Look = "look", Relationship = 2 },
            [9] = new() { Id = 9, Username = "Other", Relationship = 0 }
        };
        var (target, _) = HabbiconTestSupport.Client(new Habbo
        {
            Id = 17,
            Messenger = new(friends, [], [], new FixedTimeProvider(FixedTimeProvider.Epoch))
        });
        var clients = Proxy.Create<IGameClientManager>((method, args) =>
            method.Name == nameof(IGameClientManager.GetClientByUserId) && (int)args![0]! == 17 ? target : null);
        var loads = 0;
        var loader = Proxy.Create<IMessengerDataLoader>((method, args) =>
        {
            Assert.Equal(nameof(IMessengerDataLoader.GetRelationshipsForUserAsync), method.Name);
            Assert.Equal(18, args![0]);
            loads++;
            return Task.FromResult(new Dictionary<int, (MessengerBuddy buddy, int count)>
            {
                [1] = (new() { Id = 20, Username = "Stored", Look = "stored" }, 4)
            });
        });
        var service = new UserSocialShowcaseService(loader, clients, Unused<IIgnoredUsersService>(), Unused<IGroupManager>());
        await service.ShowRelationships(viewer, 17);
        Assert.Equal(0, loads);
        Assert.Equal(ServerPacketHeader.GetRelationshipsComposer, Assert.Single(sent).Header);
        Assert.Equal(17, ReadInt(sent[0].Payload, 0));
        Assert.Equal(1, ReadInt(sent[0].Payload, 4));
        Assert.Equal(2, ReadInt(sent[0].Payload, 8));
        Assert.Equal(2, ReadInt(sent[0].Payload, 12));
        await service.ShowRelationships(viewer, 18);
        Assert.Equal(1, loads);
        Assert.Equal(2, sent.Count);
        Assert.Equal(18, ReadInt(sent[1].Payload, 0));
        Assert.Equal(1, ReadInt(sent[1].Payload, 8));
        Assert.Equal(4, ReadInt(sent[1].Payload, 12));
    }

    [Fact]
    public async Task IgnoredLookupReceivesPreparedIdsAndMissingRoomSendsNothing()
    {
        var habbo = new Habbo { Id = 42, IgnoresComponent = new([7, 8]) };
        var (viewer, sent) = HabbiconTestSupport.Client(habbo);
        var ignored = Proxy.Create<IIgnoredUsersService>((method, args) =>
        {
            Assert.Equal(nameof(IIgnoredUsersService.GetIgnoredUsersByName), method.Name);
            Assert.Equal(new[] { 7, 8 }, (IReadOnlyCollection<int>)args![0]!);
            return Task.FromResult(new List<string> { "Alice", "Bob" });
        });
        var service = new UserSocialShowcaseService(Unused<IMessengerDataLoader>(), Unused<IGameClientManager>(), ignored, Unused<IGroupManager>());
        await service.ShowGroupBadges(viewer);
        Assert.Empty(sent);
        await service.ShowIgnoredUsers(viewer);
        Assert.Equal(ServerPacketHeader.IgnoredUsersComposer, Assert.Single(sent).Header);
    }

    private static int ReadInt(byte[] data, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));

    private static object[] Compose(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes.ToArray();
    }

    private static T Unused<T>() where T : class => Proxy.Create<T>((method, _) => throw new InvalidOperationException(method.Name));

    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Callback { get; set; } = null!;
        public static T Create<T>(Func<MethodInfo, object?[]?, object?> callback) where T : class
        {
            var instance = DispatchProxy.Create<T, Proxy>();
            ((Proxy)(object)instance).Callback = callback;
            return instance;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Callback(method!, args);
    }

    private sealed class RecordingShowcase : IUserSocialShowcaseService
    {
        public int UserId { get; private set; }
        public int IgnoredViews { get; private set; }
        public int GroupViews { get; private set; }
        public Task ShowRelationships(GameClient session, int userId) { UserId = userId; return Task.CompletedTask; }
        public Task ShowIgnoredUsers(GameClient session) { IgnoredViews++; return Task.CompletedTask; }
        public Task ShowGroupBadges(GameClient session) { GroupViews++; return Task.CompletedTask; }
    }
}
