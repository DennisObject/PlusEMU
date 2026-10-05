using System.Buffers.Binary;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.FriendList;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public sealed class MessengerFriendHandlerTests
{
    [Fact]
    public async Task AcceptReadsTheWholeFrameBeforeAnyMutation()
    {
        var calls = new List<string>();
        var service = new RecordingFriends(calls);
        var session = Session();

        await new AcceptFriendEvent(service).Parse(session, Packet(2, 5, 6));
        Assert.Equal(new[] { "accept 5", "accept 6" }, calls);

        calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new AcceptFriendEvent(service).Parse(session, Packet(2, 5)));
        Assert.Empty(calls);

        await new AcceptFriendEvent(service).Parse(session, Packet(-1));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task AcceptCapsTheRequestedCountAtFifty()
    {
        var calls = new List<string>();
        var values = new object[51 + 1];
        values[0] = 51;
        for (var i = 1; i < values.Length; i++) values[i] = i;

        await new AcceptFriendEvent(new RecordingFriends(calls)).Parse(Session(), Packet(values));

        Assert.Equal(50, calls.Count);
    }

    [Fact]
    public async Task DeclineHandlesDeclineAllAndSingleRequestsWithoutPartialReads()
    {
        var calls = new List<string>();
        var service = new RecordingFriends(calls);
        var session = Session();

        await new DeclineFriendEvent(service).Parse(session, Packet(true, 0));
        Assert.Equal(new[] { "decline-all" }, calls);

        calls.Clear();
        await new DeclineFriendEvent(service).Parse(session, Packet(false, 0, 9));
        Assert.Equal(new[] { "decline 9" }, calls);

        calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new DeclineFriendEvent(service).Parse(session, Packet(false, 0)));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task RemoveReadsTheWholeFrameBeforeAnyMutation()
    {
        var calls = new List<string>();
        var service = new RecordingFriends(calls);
        var session = Session();

        await new RemoveFriendEvent(service).Parse(session, Packet(2, 5, 6));
        Assert.Equal(new[] { "remove 5,6" }, calls);

        calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new RemoveFriendEvent(service).Parse(session, Packet(2, 5)));
        Assert.Empty(calls);
    }

    private static FlashGameClient Session()
    {
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        client.SetHabbo(new Habbo { Id = 1, Username = "Alice", Access = UserAccess.Empty });
        return client;
    }

    private static IIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = new byte[4];
            switch (value)
            {
                case int number: BinaryPrimitives.WriteInt32BigEndian(bytes, number); stream.Write(bytes); break;
                case bool flag: stream.WriteByte(flag ? (byte)1 : (byte)0); break;
                default: throw new NotSupportedException();
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private sealed class RecordingFriends(List<string> calls) : IMessengerFriendMutationService
    {
        public Task<FriendRequestError?> AcceptRequestAsync(Habbo habbo, int fromId) { calls.Add($"accept {fromId}"); return Task.FromResult<FriendRequestError?>(null); }
        public Task<FriendRequestError?> DeclineRequestAsync(Habbo habbo, int fromId) { calls.Add($"decline {fromId}"); return Task.FromResult<FriendRequestError?>(null); }
        public Task DeclineAllRequestsAsync(Habbo habbo) { calls.Add("decline-all"); return Task.CompletedTask; }
        public Task<FriendRequestOutcome> SendRequestAsync(Habbo habbo, int toId) => throw new NotSupportedException();
        public Task RemoveFriendsAsync(Habbo habbo, IReadOnlyList<int> friendIds) { calls.Add("remove " + string.Join(",", friendIds)); return Task.CompletedTask; }
    }
}
