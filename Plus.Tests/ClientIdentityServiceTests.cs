using Plus.Communication.Packets.Incoming.Handshake;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ClientIdentityServiceTests
{
    [Fact]
    public async Task IdentityHandlerDecodesBothStringsBeforeDelegatingTheMachineId()
    {
        var identity = new RecordingIdentity();
        var packet = HabbiconTestSupport.Incoming("ignored", "machine");
        await new UniqueIdEvent(identity).Parse(null!, packet);
        Assert.Equal("machine", identity.MachineId);
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public async Task TruncatedIdentityFrameDoesNotDelegate()
    {
        var identity = new RecordingIdentity();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new UniqueIdEvent(identity).Parse(null!, HabbiconTestSupport.Incoming("ignored")));
        Assert.Null(identity.MachineId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdentityIsCapturedBeforeTheBanCheckAndOnlyAnAllowedMachineGetsAReply(bool banned)
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var disconnected = false;
        client.DisconnectRequested = () => disconnected = true;
        var moderation = CatalogSnapshotTestSupport.Proxy<IModerationManager>((method, args) =>
        {
            Assert.Equal(nameof(IModerationManager.HasMachineBanCheck), method);
            Assert.Equal("machine", args![0]);
            Assert.Equal("machine", client.MachineId);
            Assert.False(disconnected);
            Assert.Empty(sent);
            return banned;
        });

        new ClientIdentityService(moderation).SetMachineIdentity(client, "machine");

        Assert.Equal(banned, disconnected);
        Assert.Equal(banned, client.Closed.IsCancellationRequested);
        if (banned) Assert.Empty(sent);
        else
        {
            var response = Assert.Single(sent);
            Assert.Equal(ServerPacketHeader.SetUniqueIdComposer, response.Header);
            var body = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = response.Payload };
            Assert.Equal("machine", body.ReadString());
            Assert.False(body.HasDataRemaining());
        }
    }

    private sealed class RecordingIdentity : IClientIdentityService
    {
        public string? MachineId;
        public void SetMachineIdentity(GameClient session, string machineId) => MachineId = machineId;
    }
}
