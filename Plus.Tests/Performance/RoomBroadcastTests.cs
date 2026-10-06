using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests.Performance;

public class RoomBroadcastTests
{
    [Fact]
    public void FiveHundredRecipientsReceiveOneSnapshotWithTheirRevisionHeaders()
    {
        var fixture = RoomPerformanceFixture.Create(0, 500);
        var received = new List<ReadOnlyMemory<byte>>();
        var changedRevision = new Revision { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
        {
            [ServerPacketHeader.UserUpdateComposer] = 999
        } };
        fixture.Clients[^1].Revision = changedRevision;
        foreach (var client in fixture.Clients)
            client.SendCallback = args => { received.Add(args.MemoryBuffer); return true; };
        var composer = new CountingPacket(new UserUpdateComposer(RoomUserStatusSnapshot.Capture(fixture.Users)));
        var expected = Encode(fixture.Clients[0], composer);
        composer.Count = 0;

        fixture.Room.SendPacket(composer);

        Assert.Equal(2, composer.Count);
        Assert.Equal(500, received.Count);
        foreach (var buffer in received.Take(499)) Assert.Equal(expected, buffer.ToArray());
        Assert.Equal(999, FlashGameClient.DecodeInt16(received[^1].Slice(4)));
        Assert.Equal(expected.AsSpan(6).ToArray(), received[^1].Slice(6).ToArray());
        Assert.True(MemoryMarshal.TryGetArray(received[0], out var first));
        Assert.True(MemoryMarshal.TryGetArray(received[498], out var last));
        Assert.Same(first.Array, last.Array);

        fixture.Users[0].X = 3;
        fixture.Users[0].Statusses["mv"] = "3,1,0";
        fixture.Room.SendPacket(composer);
        Assert.Equal(4, composer.Count);
        Assert.Equal(expected, received[0].ToArray());
        Assert.Equal(expected, received[500].ToArray());

        var nextComposer = new CountingPacket(new UserUpdateComposer(RoomUserStatusSnapshot.Capture(fixture.Users)));
        fixture.Room.SendPacket(nextComposer);
        Assert.Equal(2, nextComposer.Count);
        Assert.Equal(1500, received.Count);
        Assert.NotEqual(expected, received[1000].ToArray());
    }

    [Fact]
    public void PacketFactoryAndClientHeaderImplementationStayIsolated()
    {
        var sharedFactory = new FlashPacketFactory();
        var revision = new Revision { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [ServerPacketHeader.ChatComposer] = 222 } };
        var clients = new FlashGameClient[]
        {
            new(TestGameServer.Instance, sharedFactory, TestLogging.GameClient),
            new(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient),
            new AlternateHeaderClient(sharedFactory)
        };
        var received = new List<byte[]>();
        foreach (var client in clients)
        {
            client.Revision = revision;
            client.SendCallback = args => { received.Add(args.MemoryBuffer.ToArray()); return false; };
        }
        var composer = new CountingPacket(new ChatComposer(1, "hello", 0, 2));
        GameClient.SendBroadcast(composer, clients);
        Assert.Equal(3, composer.Count);
        Assert.Equal(received[0], received[1]);
        Assert.Equal(223, FlashGameClient.DecodeInt16(received[2].AsMemory(4)));
    }

    [Fact]
    public void SingleSendKeepsItsBytesAfterPooledStreamReuse()
    {
        var client = RoomPerformanceFixture.Create(0, 1).Clients[0];
        ReadOnlyMemory<byte> pending = default;
        client.SendCallback = args => { pending = args.MemoryBuffer; return true; };
        var composer = new ChatComposer(1, "pending async TCP send", 0, 2);
        client.Send(composer);
        var expected = pending.ToArray();
        for (var i = 0; i < 10; i++)
        {
            using var stream = PlusMemoryStream.GetStream();
            stream.Write(new byte[expected.Length]);
        }
        Assert.Equal(expected, pending.ToArray());
    }

    [Fact]
    public void SynchronousAndCopiedWebSocketSendsDisposeTheirEventArgs()
    {
        var client = RoomPerformanceFixture.Create(0, 1).Clients[0];
        SocketAsyncEventArgs? synchronous = null;
        client.SendCallback = args => { synchronous = args; return false; };
        var composer = new ChatComposer(1, "test", 0, 2);
        client.Send(composer);
        Assert.Throws<ObjectDisposedException>(() => synchronous!.SetBuffer(Array.Empty<byte>()));

        SocketAsyncEventArgs? websocket = null;
        byte[]? queued = null;
        client.SendCallback = args =>
        {
            websocket = args;
            queued = args.MemoryBuffer.ToArray();
            // WsSessionProxy reports no pending args even when the WebSocket queue accepts data.
            return false;
        };
        client.Send(composer);
        Assert.Throws<ObjectDisposedException>(() => websocket!.SetBuffer(Array.Empty<byte>()));
        Assert.Equal(Encode(client, composer), queued);
    }

    [Fact]
    public void PendingTcpSendDisposesEventArgsOnlyAfterCompletion()
    {
        var client = RoomPerformanceFixture.Create(0, 1).Clients[0];
        SocketAsyncEventArgs? pending = null;
        client.SendCallback = args => { pending = args; return true; };
        var composer = new ChatComposer(1, "test", 0, 2);
        client.Send(composer);
        Assert.Equal(Encode(client, composer), pending!.MemoryBuffer.ToArray());
        // Raise the same event as a native async completion, without opening a socket.
        typeof(SocketAsyncEventArgs).GetMethod("OnCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(pending, new object[] { pending });
        Assert.Throws<ObjectDisposedException>(() => pending.SetBuffer(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(false, 0, 2)]
    [InlineData(false, 7, 7)]
    [InlineData(true, 7, 0)]
    public void BotAndPetChatPreserveSpeechPreferencesAndBubble(bool pet, int colour, int expectedColour)
    {
        var fixture = RoomPerformanceFixture.Create(1, 2);
        var bot = fixture.Bots[0];
        bot.BotData.AiType = pet ? BotAiType.Pet : BotAiType.Generic;
        var muted = fixture.Clients[1].GetHabbo();
        muted.AllowBotSpeech = true;
        muted.AllowPetSpeech = true;
        var received = new List<byte[]>();
        fixture.Clients[0].SendCallback = args => { received.Add(args.MemoryBuffer.ToArray()); return false; };
        fixture.Clients[1].SendCallback = _ => throw new InvalidOperationException("Speech preference was ignored");
        bot.Chat("test", colour);
        Assert.Equal(Encode(fixture.Clients[0], new ChatComposer(bot.VirtualId, "test", 0, expectedColour)), Assert.Single(received));
    }

    [Fact]
    public void RightsOnlyBroadcastKeepsItsRecipientFilter()
    {
        var fixture = RoomPerformanceFixture.Create(1, 2);
        fixture.Room.Type = "private";
        fixture.Room.OwnerName = "owner";
        fixture.Room.UsersWithRights = new();
        fixture.Clients[0].GetHabbo().Username = "owner";
        fixture.Clients[1].GetHabbo().Username = "visitor";
        fixture.Clients[1].GetHabbo().Access = UserAccess.Empty;
        var ownerPackets = 0;
        fixture.Clients[0].SendCallback = _ => { ownerPackets++; return false; };
        fixture.Clients[1].SendCallback = _ => throw new InvalidOperationException("Visitor received a rights-only packet");
        fixture.Room.SendPacket(new UserUpdateComposer(RoomUserStatusSnapshot.Capture(fixture.Bots)), withRightsOnly: true);
        Assert.Equal(1, ownerPackets);
        Assert.Null(fixture.Bots[0].GetClient());
        Assert.Same(fixture.Clients[0], fixture.Users[0].GetClient());
    }

    [Fact]
    public void StatusCollectionSendsEachChangedAvatarOnceAndClearsItsFlag()
    {
        var fixture = RoomPerformanceFixture.Create(500, 1);
        foreach (var user in fixture.Users) user.UpdateNeeded = false;
        var received = new List<byte[]>();
        fixture.Clients[0].SendCallback = args => { received.Add(args.MemoryBuffer.ToArray()); return false; };
        fixture.Manager.SerializeStatusUpdates();
        Assert.Equal(500, FlashGameClient.DecodeInt32(Assert.Single(received).AsMemory(6)));
        Assert.All(fixture.Bots, bot => Assert.False(bot.UpdateNeeded));
        fixture.Manager.SerializeStatusUpdates();
        Assert.Single(received);
    }

    [Fact]
    public void FiveHundredOverlappingStressBotsWalkWithoutChangingFloorGrid()
    {
        var fixture = RoomPerformanceFixture.Create(500, 0);
        var initial = (byte[,])fixture.Map.GameMap.Clone();
        fixture.PrepareWalking();
        fixture.Manager.OnCycle();
        Assert.All(fixture.Bots, bot => Assert.True(bot.SetStep));
        fixture.Manager.OnCycle();
        Assert.All(fixture.Bots, bot => Assert.Equal(2, bot.X));
        Assert.Equal(500, fixture.Map.GetRoomUsers(new(2, 1)).Count);
        for (var x = 0; x < 4; x++) for (var y = 0; y < 4; y++) Assert.Equal(initial[x, y], fixture.Map.GameMap[x, y]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void FallbackRemovalRestoresReservationsOnlyForOrdinaryBots(bool temporary, bool pendingStep)
    {
        var fixture = RoomPerformanceFixture.Create(1, 0);
        var bot = fixture.Bots[0];
        typeof(RoomBot).GetProperty(nameof(RoomBot.IsTemporary))!.SetValue(bot.BotData, temporary);
        bot.SetStep = pendingStep;
        bot.SetX = 2;
        bot.SetY = 1;
        typeof(RoomUserManager).GetMethod("RemoveRoomUser", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Manager, new object[] { bot, false, false });
        Assert.Empty(fixture.Manager.GetUserList());
        Assert.Empty(fixture.Map.GetRoomUsers(new(1, 1)));
        Assert.Equal(temporary ? (byte)1 : bot.SqState, fixture.Map.GameMap[pendingStep ? 2 : 1, 1]);
    }

    [Fact]
    public void OrdinaryBotStillUsesExistingFloorReservationRules()
    {
        var fixture = RoomPerformanceFixture.Create(1, 0);
        var bot = fixture.Bots[0];
        typeof(RoomBot).GetProperty(nameof(RoomBot.IsTemporary))!.SetValue(bot.BotData, false);
        bot.SqState = 2;
        fixture.PrepareWalking();
        fixture.Manager.OnCycle();
        Assert.True(bot.SetStep);
        Assert.Equal(2, fixture.Map.GameMap[1, 1]);
        Assert.Equal(1, bot.SqState);
    }

    private static byte[] Encode(FlashGameClient client, IServerPacket composer)
    {
        using var stream = PlusMemoryStream.GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));
        var memory = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        client.CreateHeader(memory, client.Revision.InternalIdToOutgoingIdMapping[composer.MessageId]);
        return memory.ToArray();
    }

    private sealed class CountingPacket(IServerPacket inner) : IServerPacket
    {
        public uint MessageId => inner.MessageId;
        public int Count { get; set; }
        public void Compose(IOutgoingPacket packet) { Count++; inner.Compose(packet); }
    }

    private sealed class AlternateHeaderClient(IPacketFactory factory) : FlashGameClient(TestGameServer.Instance, factory, TestLogging.GameClient)
    {
        public override void CreateHeader(Memory<byte> memory, uint messageId) => base.CreateHeader(memory, messageId + 1);
    }
}
