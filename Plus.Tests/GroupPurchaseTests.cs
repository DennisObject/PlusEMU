using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Groups;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

[CollectionDefinition("Group purchase", DisableParallelization = true)]
public class GroupPurchaseCollection;

[Collection("Group purchase")]
public class GroupPurchaseTests
{
    private readonly RoomData _room = new() { Id = 42, OwnerId = 7 };
    private readonly Group _group = GroupPurchaseTestSupport.Group(99);
    private readonly TestClient _client = new();
    private readonly PurchaseGroupEvent _handler;
    private string? _createdBadge;
    private bool _createSucceeds = true;
    private bool _creationThrows;
    private string _cost = "150";

    public GroupPurchaseTests()
    {
        _client.SetHabbo(new Habbo { Id = 7, Credits = 1000, Access = Plus.HabboHotel.Permissions.UserAccess.Create([], [new(Plus.HabboHotel.Permissions.PermissionKeys.ClubAccess, false)]) });
        var roomLoader = Proxy<IRoomDataLoader>((method, args) =>
        {
            Assert.Equal(nameof(IRoomDataLoader.TryGetData), method);
            Assert.Equal((uint)42, args[0]);
            args[1] = _room;

            return true;
        });
        var groups = Proxy<IGroupManager>((method, args) =>
        {
            Assert.Equal("TryCreateGroup", method);
            Assert.Equal(1000, _client.GetHabbo().Credits);
            Assert.Null(_room.Group);
            Assert.Equal(0, _room.GroupId);
            Assert.Empty(_client.Sent);
            Assert.Equal("test", args[1]);
            Assert.Equal("description", args[2]);
            Assert.Equal((uint)42, args[3]);
            Assert.Equal(1, args[5]);
            Assert.Equal(1, args[6]);
            _createdBadge = (string)args[4]!;

            if (_creationThrows)
            {
                throw new InvalidOperationException("write failed");
            }

            args[7] = _group;

            return _createSucceeds;
        });
        var filter = Proxy<IWordFilterManager>((_, args) => args[0]);
        var settings = Proxy<ISettingsManager>((_, _) => _cost);
        _handler = new PurchaseGroupEvent(new GroupPurchaseService(
            groups, roomLoader, filter, settings, new AccountSessionGate(),
            TestLogging.For<GroupPurchaseService>()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task SelectedBadgePartsCreateGroupAndChargeOnce(int partCount)
    {
        var packet = PurchasePacket(partCount * 3, partCount);
        await _handler.Parse(_client, packet);

        Assert.Empty(packet.Buffer.ToArray());
        Assert.Equal("b01014" + string.Concat(Enumerable.Repeat("s02024", partCount - 1)), _createdBadge);
        Assert.Same(_group, _room.Group);
        Assert.Equal(_group.Id, _room.GroupId);
        Assert.Equal(850, _client.GetHabbo().Credits);
        Assert.Contains(ServerPacketHeader.NewGroupInfoComposer, _client.Sent);
        Assert.Contains(ServerPacketHeader.CreditBalanceComposer, _client.Sent);
        Assert.Equal([
            ServerPacketHeader.CreditBalanceComposer,
            ServerPacketHeader.PurchaseOKComposer,
            ServerPacketHeader.RoomForwardComposer,
            ServerPacketHeader.NewGroupInfoComposer
        ], _client.Sent);

        await _handler.Parse(_client, PurchasePacket(partCount * 3, partCount));
        Assert.Equal(850, _client.GetHabbo().Credits);
        Assert.Single(_client.Sent, id => id == ServerPacketHeader.NewGroupInfoComposer);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 1)]
    [InlineData(18, 6)]
    [InlineData(6, 1)]
    public async Task MalformedBadgeDoesNotChargeOrCreate(int count, int parts)
    {
        await _handler.Parse(_client, PurchasePacket(count, parts));
        AssertUnpurchased();
    }

    [Fact]
    public async Task AnotherOwnersRoomDoesNotChargeOrCreate()
    {
        _room.OwnerId = 8;
        await _handler.Parse(_client, PurchasePacket(3, 1));
        AssertUnpurchased();
    }

    [Fact]
    public async Task ExistingRoomGroupDoesNotChargeOrCreate()
    {
        _room.Group = _group;
        _room.GroupId = _group.Id;

        await _handler.Parse(_client, PurchasePacket(3, 1));

        Assert.Equal(1000, _client.GetHabbo().Credits);
        Assert.Null(_createdBadge);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task NoClubAccessDoesNotChargeOrCreate()
    {
        _client.GetHabbo().Access = Plus.HabboHotel.Permissions.UserAccess.Empty;
        await _handler.Parse(_client, PurchasePacket(3, 1));
        AssertUnpurchased();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("invalid")]
    public async Task InvalidConfiguredCostDoesNotChargeOrCreate(string cost)
    {
        _cost = cost;
        await _handler.Parse(_client, PurchasePacket(3, 1));
        AssertUnpurchased();
    }

    [Fact]
    public async Task InsufficientCreditsDoNotCreateGroup()
    {
        _client.GetHabbo().Credits = 149;
        await _handler.Parse(_client, PurchasePacket(3, 1));
        Assert.Equal(149, _client.GetHabbo().Credits);
        Assert.Null(_createdBadge);
        Assert.Null(_room.Group);
        Assert.Equal(0, _room.GroupId);
    }

    [Fact]
    public async Task FailedCreationDoesNotCharge()
    {
        _createSucceeds = false;
        await _handler.Parse(_client, PurchasePacket(3, 1));
        Assert.Equal(1000, _client.GetHabbo().Credits);
        Assert.Null(_room.Group);
        Assert.DoesNotContain(ServerPacketHeader.CreditBalanceComposer, _client.Sent);
        Assert.DoesNotContain(ServerPacketHeader.NewGroupInfoComposer, _client.Sent);
        Assert.Equal(0, _room.GroupId);
    }

    [Fact]
    public async Task CurrentRoomPurchaseDoesNotSendRoomForward()
    {
        var currentRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        typeof(Room).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(currentRoom, _room);
        _client.GetHabbo().CurrentRoom = currentRoom;

        await _handler.Parse(_client, PurchasePacket(3, 1));

        Assert.Equal(_group.Id, _room.GroupId);
        Assert.Same(_group, _room.Group);
        Assert.Equal([
            ServerPacketHeader.CreditBalanceComposer,
            ServerPacketHeader.PurchaseOKComposer,
            ServerPacketHeader.NewGroupInfoComposer
        ], _client.Sent);
    }

    [Fact]
    public async Task ClosedWalletDoesNotCreateOrPublish()
    {
        typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_client.GetHabbo(), true);

        await _handler.Parse(_client, PurchasePacket(3, 1));

        AssertUnpurchased();
    }

    [Fact]
    public async Task CreationExceptionDoesNotChargeOrPublish()
    {
        _creationThrows = true;

        await _handler.Parse(_client, PurchasePacket(3, 1));

        Assert.Equal(1000, _client.GetHabbo().Credits);
        Assert.Null(_room.Group);
        Assert.Equal(0, _room.GroupId);
        Assert.DoesNotContain(ServerPacketHeader.CreditBalanceComposer, _client.Sent);
        Assert.DoesNotContain(ServerPacketHeader.NewGroupInfoComposer, _client.Sent);
    }

    private void AssertUnpurchased()
    {
        Assert.Equal(1000, _client.GetHabbo().Credits);
        Assert.Null(_createdBadge);
        Assert.Null(_room.Group);
        Assert.Equal(0, _room.GroupId);
        Assert.Empty(_client.Sent);
    }

    private static FlashIncomingPacket PurchasePacket(int count, int parts)
    {
        using var stream = new MemoryStream();

        foreach (var text in new[] { "test", "description" })
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }

        var values = new List<int> { 42, 1, 1, count };

        for (var part = 0; part < parts; part++)
        {
            values.AddRange(new[] { part == 0 ? 1 : 2, part == 0 ? 1 : 2, 4 });
        }

        foreach (var value in values)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

        return new FlashIncomingPacket { Buffer = stream.ToArray() };
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

    private sealed class TestClient : GameClient
    {
        public List<uint> Sent { get; } = new();
        public TestClient() : base(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = new Revision
            {
                InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields()
                    .Where(field => field.IsLiteral && field.FieldType == typeof(uint))
                    .Select(field => (uint)field.GetRawConstantValue()!).Distinct().ToDictionary(id => id)
            };
            SendCallback = _ => false;
        }
        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);
        public override void CreateHeader(Memory<byte> memory, uint messageId) => Sent.Add(messageId);
    }

}

internal static class GroupPurchaseTestSupport
{
    internal static Group Group(int id)
    {
        var group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
        group.Id = id;

        return group;
    }
}
