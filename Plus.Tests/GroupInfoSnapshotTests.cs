using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Database;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("Group purchase")]
public class GroupInfoSnapshotTests : IDisposable
{
    // SHA-256 of the 120 pre-migration GroupInfoComposer payloads (type x viewer x newWindow x forum x adminOnlyDeco).
    private const string BaselineSha256 = "b50ca1fc5454747df4f934099fbf05d22151c9b004328873e8f0c0c17797426c";

    private static readonly FieldInfo DatabaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _previousDatabase;
    private readonly GroupManagementTests.RecordingDatabase _database = new();
    private readonly Dictionary<int, GameClient> _clients = new();

    public GroupInfoSnapshotTests()
    {
        _previousDatabase = DatabaseField.GetValue(null);
        DatabaseField.SetValue(null, _database);
        foreach (var (id, name) in new[] { (7, "Owner"), (4, "Admin"), (5, "Requester"), (3, "Member"), (9, "Outsider") })
            _clients[id] = HabbiconTestSupport.Client(new Habbo { Id = id, Username = name }).Client;
    }

    public void Dispose() => DatabaseField.SetValue(null, _previousDatabase);

    [Fact]
    public void ComposedBytesMatchPreMigrationBaseline()
    {
        var service = Service(_clients, _ => null);
        var lines = new List<string>();
        foreach (var type in new[] { 0, 1, 2 })
        foreach (var viewer in new[] { 7, 4, 5, 3, 9 })
        foreach (var newWindow in new[] { false, true })
        foreach (var forum in new[] { false, true })
        foreach (var adminOnly in new[] { 0, 1 })
        {
            var snapshot = service.Capture(NewGroup(type, forum, adminOnly), viewer);
            lines.Add($"t{type} v{viewer} w{newWindow} f{forum} a{adminOnly}: {Writes(snapshot, newWindow)}");
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n")));
        Assert.Equal(120, lines.Count);
        Assert.Equal(BaselineSha256, digest);
    }

    [Fact]
    public void RecomposingSnapshotIsStableAfterGroupMutation()
    {
        var group = NewGroup(type: 1, forum: true, adminOnly: 0);
        var snapshot = Service(_clients, _ => null).Capture(group, 4);
        var first = Writes(snapshot, true);

        group.Name = "Changed";
        group.Badge = "b05114s06114";
        group.AddMember(30);
        group.MakeAdmin(3);

        Assert.Equal(first, Writes(snapshot, true));
        Assert.NotEqual(first, Writes(Service(_clients, _ => null).Capture(group, 4), true));
    }

    [Theory]
    [InlineData("session", "Owner")]
    [InlineData("cache", "Cached")]
    public void CreatorNameUsesSessionThenCache(string source, string expected)
    {
        var clients = source == "session" ? _clients : new Dictionary<int, GameClient>();
        var snapshot = Service(clients, id => source == "cache" ? new CachedUser { Id = id, Username = "Cached", Look = "hr-1" } : null)
            .Capture(NewGroup(type: 0, forum: true, adminOnly: 0), 9);

        Assert.Equal(expected, snapshot.CreatorName);
    }

    private static GroupInfoSnapshotService Service(IReadOnlyDictionary<int, GameClient> clients, Func<int, CachedUser?> users)
    {
        var clientManager = Proxy<IGameClientManager>((method, args) =>
            method == "GetClientByUserId" ? clients.GetValueOrDefault((int)args[0]!) : throw new InvalidOperationException(method));
        var cache = Proxy<ICacheManager>((method, args) =>
            method == "GenerateUser" ? users((int)args[0]!) : throw new InvalidOperationException(method));
        return new GroupInfoSnapshotService(clientManager, cache, new GroupManagementTests.RecordingDatabase());
    }

    private static string Writes(GroupInfoSnapshot snapshot, bool newWindow)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new GroupInfoComposer(snapshot, newWindow).Compose(packet);
        return string.Join("|", packet.Writes.Select(write => $"{write.GetType().Name}:{write}"));
    }

    private static Group NewGroup(int type, bool forum, int adminOnly)
    {
        var group = new Group(9, "Crew", "desc", "b01014s02024", 42, 7, 1_700_000_000, type, 3, 4, adminOnly, forum);
        var room = (RoomData)RuntimeHelpers.GetUninitializedObject(typeof(RoomData));
        room.Id = 42;
        room.Name = "HQ";
        typeof(Group).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(group, room);
        group.AddMember(4);
        group.MakeAdmin(4);
        group.AddMember(3);
        group.AddMember(5);
        group.AddMember(8);
        var requests = (List<int>)typeof(Group).GetField("_requests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group)!;
        requests.Add(5);
        requests.Add(6);
        return group;
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;
        return proxy;
    }

    public class TestProxy : System.Reflection.DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }
}
