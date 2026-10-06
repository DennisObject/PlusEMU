using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RoomBannedUsersSnapshotTests
{
    [Fact]
    public void ComposerKeepsCapturedIdentityAndFallbackFields()
    {
        var composer = new GetRoomBannedUsersComposer(new(42, [new(7, "guest"), new(0, "Unknown Error")]));
        var first = new HabbiconTestSupport.RecordingPacket(); composer.Compose(first);
        var second = new HabbiconTestSupport.RecordingPacket(); composer.Compose(second);
        Assert.Equal(new object[] { (uint)42, 2, 7, "guest", 0, "Unknown Error" }, first.Writes);
        Assert.Equal(first.Writes, second.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyOwnerCanResolveBannedUserIdentities(bool owner)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42; room.OwnerName = "owner"; room.Type = "private";
        var bans = new Store();
        typeof(Room).GetField("_bansComponent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new BansComponent(room, bans, TimeProvider.System, []));
        var resolved = new List<int>();
        var cache = DispatchProxy.Create<ICacheManager, Lookup>();
        ((Lookup)(object)cache).Read = id => { resolved.Add(id); return id == 7 ? new CachedUser { Id = id, Username = "guest" } : null; };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = owner ? "owner" : "visitor", CurrentRoom = room, Access = EditorTestSupport.Access([]) });
        new RoomBannedUsersService(cache).Send(client);
        Assert.Equal(owner ? new[] { 7, 8 } : [], resolved);
        Assert.Equal(owner ? 1 : 0, sent.Count);
        Assert.Equal(owner ? 1 : 0, bans.Reads);
    }

    public class Lookup : DispatchProxy
    {
        public Func<int, CachedUser?> Read = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "GenerateUser"
            ? Read((int)args![0]!) : throw new NotSupportedException(method.Name);
    }
    private sealed class Store : IRoomBanStore
    {
        public int Reads { get; private set; }
        public IEnumerable<RoomBan> Load(uint roomId) => [];
        public IEnumerable<int> ActiveUserIds(uint roomId) { Reads++; return [7, 8]; }
        public void Save(uint roomId, int userId, DateTimeOffset expiresAt) => throw new NotSupportedException();
        public void Delete(uint roomId, int userId) => throw new NotSupportedException();
    }
}
