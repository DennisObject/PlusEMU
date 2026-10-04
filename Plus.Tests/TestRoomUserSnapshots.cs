using System.Reflection;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.Tests;

// Fixtures bypassing the room lifecycle still exercise the production snapshot capture.
internal sealed class TestRoomUserSnapshots : IRoomUserSnapshotService
{
    public static void Install(Room room) => typeof(Room).GetField("_userSnapshots", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(room, new TestRoomUserSnapshots());

    public RoomUserSnapshot? Capture(RoomUser user)
    {
        var clients = DispatchProxy.Create<IGameClientManager, Lookup>();
        ((Lookup)(object)clients).Client = user.IsBot
            ? HabbiconTestSupport.Client(new Habbo { Id = user.BotData.OwnerId, Username = "owner" }).Client
            : user.GetClient();
        return new RoomUserSnapshotService(null!, clients, null!, null!).Capture(user);
    }

    public IReadOnlyList<RoomUserSnapshot> Capture(IEnumerable<RoomUser> users) =>
        users.Select(Capture).OfType<RoomUserSnapshot>().ToArray();

    public class Lookup : DispatchProxy
    {
        public GameClient? Client { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "GetClientByUserId"
            ? Client : throw new NotSupportedException(method.Name);
    }
}
