using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Xunit;

namespace Plus.Tests;

public sealed class RoomRightsWireTests
{
    [Theory]
    [InlineData(42u, 1)]
    [InlineData(1337u, 4)]
    [InlineData(42u, 0)]
    public void GrantIdentifiesTheRoomBeforeTheControllerLevel(uint roomId, int level)
    {
        var composer = new YouAreControllerComposer(roomId, level);
        using var stream = PlusMemoryStream.GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));
        var packet = new FlashIncomingPacket { Buffer = stream.ToArray()[6..] };

        Assert.Equal(2909u, composer.MessageId);
        Assert.Equal(roomId, packet.ReadUInt());
        Assert.Equal(level, packet.ReadInt());
        Assert.False(packet.HasDataRemaining());
    }

    [Theory]
    [InlineData(42u)]
    [InlineData(1337u)]
    public void RevocationIdentifiesExactlyTheRoom(uint roomId)
    {
        var composer = new YouAreNotControllerComposer(roomId);
        using var stream = PlusMemoryStream.GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));
        var packet = new FlashIncomingPacket { Buffer = stream.ToArray()[6..] };

        Assert.Equal(920u, composer.MessageId);
        Assert.Equal(roomId, packet.ReadUInt());
        Assert.False(packet.HasDataRemaining());
    }
}
