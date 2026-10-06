using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Rooms.FloorPlan;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class FloorPlanSaveLifecycleTests
{
    [Fact]
    public void SaveRemovesConnectedClientsBeforeUnloadThenForwardsThoseClients()
    {
        var saver = new SavedClient();
        var visitor = new SavedClient();
        var connected = new List<GameClient> { saver, visitor };
        var currentRoom = connected.ToDictionary(client => client, _ => "live");
        var managerAlive = true;
        var steps = new List<string>();

        UpdateFloorPropertiesEvent.ReturnConnectedClients(
            connected,
            (client, notifyUser) =>
            {
                Assert.True(managerAlive);
                Assert.True(notifyUser);
                currentRoom[client] = null;
                steps.Add("remove:" + client.Id);
            },
            () => steps.Add("reload"),
            () =>
            {
                steps.Add("unload");
                managerAlive = false;
            },
            client =>
            {
                Assert.False(managerAlive);
                Assert.Null(currentRoom[client]);
                steps.Add("forward:" + client.Id);
            });

        Assert.Equal(
            new[]
            {
                "remove:" + saver.Id,
                "remove:" + visitor.Id,
                "reload",
                "unload",
                "forward:" + saver.Id,
                "forward:" + visitor.Id
            },
            steps);
        Assert.All(connected, client => Assert.Null(currentRoom[client]));
    }

    [Fact]
    public void SaveClearsCurrentRoomBeforeUnloadWhenRemovalLeavesItSet()
    {
        var client = ClientStillInRoom(out var room);
        var unloaded = false;

        UpdateFloorPropertiesEvent.ReturnConnectedClients(
            new[] { client },
            (_, notifyUser) =>
            {
                Assert.True(notifyUser);
                Assert.Same(room, client.GetHabbo().CurrentRoom);
            },
            () => Assert.Null(client.GetHabbo().CurrentRoom),
            () =>
            {
                Assert.Null(client.GetHabbo().CurrentRoom);
                unloaded = true;
            },
            _ => Assert.Null(client.GetHabbo().CurrentRoom));

        Assert.True(unloaded);
        Assert.Null(client.GetHabbo().CurrentRoom);
    }

    [Fact]
    public void SaveClearsCurrentRoomWhenRemovalThrowsBeforeUnload()
    {
        var client = ClientStillInRoom(out _);

        var error = Assert.Throws<InvalidOperationException>(() =>
            UpdateFloorPropertiesEvent.ReturnConnectedClients(
                new[] { client },
                (_, _) => throw new InvalidOperationException("close failed"),
                () => throw new InvalidOperationException("reload"),
                () => throw new InvalidOperationException("unload"),
                _ => throw new InvalidOperationException("forward")));

        Assert.Equal("close failed", error.Message);
        Assert.Null(client.GetHabbo().CurrentRoom);
    }

    private static SavedClient ClientStillInRoom(out Room room)
    {
        room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var client = new SavedClient();
        client.SetHabbo(new Habbo { CurrentRoom = room });

        return client;
    }

    private sealed class SavedClient : GameClient
    {
        public SavedClient() : base(null!, null!, TestLogging.GameClient)
        {
            Id = Guid.NewGuid();
        }

        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (false, false, 0, 0, 0);

        public override void CreateHeader(Memory<byte> memory, uint messageId)
        {
        }
    }
}
