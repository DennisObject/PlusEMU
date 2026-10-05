using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Outgoing;
using Xunit;
using Plus.HabboHotel.Rooms;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V2ClickOnTheTileJustLeftReversesWithoutAnIdleTick(bool fast)
    {
        var actor = ExecutorActor(1, 1);
        actor.FastWalking = fast;
        var move = new MoveAvatarEvent(new RoomAvatarActionService(TimeProvider.System, null!, null!));
        await move.Parse(_client, ClientPacket(2, 1));
        ExecutorTick();
        Assert.Equal("2,1,0", actor.Statusses["mv"]);
        await move.Parse(_client, ClientPacket(1, 1)); // mid-step: the server still has X = 1
        ExecutorTick();
        Assert.Equal((2, 1), (actor.X, actor.Y));
        Assert.Equal("1,1,0", actor.Statusses.GetValueOrDefault("mv"));
        Assert.Equal(6, actor.RotBody);
        await move.Parse(_client, ClientPacket(2, 1));
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal("2,1,0", actor.Statusses.GetValueOrDefault("mv"));
        ExecutorTick();
        Assert.Equal((2, 1), (actor.X, actor.Y));
        Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public async Task V2IdleClickOnOwnTileSendsNothing()
    {
        ExecutorActor(1, 1);
        await new MoveAvatarEvent(new RoomAvatarActionService(TimeProvider.System, null!, null!)).Parse(_client, ClientPacket(1, 1));
        ExecutorTick();
        Assert.DoesNotContain(_client.Packets, p => p.Header == ServerPacketHeader.UserUpdateComposer);
    }
}
