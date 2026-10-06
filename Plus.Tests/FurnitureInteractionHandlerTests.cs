using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public class FurnitureInteractionHandlerTests
{
    [Theory]
    [InlineData("off", false)]
    [InlineData("roll", true)]
    [InlineData("gate", false)]
    [InlineData("wall", true)]
    public async Task CompleteFrameDelegatesOnceWithoutLookingUpRoomOrSession(string action, bool parameter)
    {
        var service = new RecordingFurniture();
        var handler = Handler(action, service);
        await handler.Parse(null!, null!, parameter ? Packet(-5, 17) : Packet(-5));

        Assert.Equal((action, unchecked((uint)-5), parameter ? 17 : -1), Assert.Single(service.Calls));
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("roll", true)]
    [InlineData("gate", false)]
    [InlineData("wall", true)]
    public async Task TruncatedFrameNeverDelegates(string action, bool parameter)
    {
        var service = new RecordingFurniture();
        var handler = Handler(action, service);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => handler.Parse(null!, null!, parameter ? Packet(7) : Packet()));
        Assert.Empty(service.Calls);
    }

    private static RoomPacketEvent Handler(string action, IFurnitureUseService service) => action switch
    {
        "off" => new DiceOffEvent(service),
        "roll" => new ThrowDiceEvent(service),
        "gate" => new OneWayGateEvent(service),
        "wall" => new UseWallItemEvent(service),
        _ => throw new ArgumentException(nameof(action))
    };

    private static FlashIncomingPacket Packet(params int[] values)
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = new FlashOutgoingPacket(stream);

        foreach (var value in values)
        {
            packet.WriteInteger(value);
        }

        return new()
        {
            Buffer = stream.ToArray().AsMemory(6)
        };
    }

    private sealed class RecordingFurniture : IFurnitureUseService
    {
        public List<(string, uint, int)> Calls { get; } = [];
        public void TurnOffDice(Room room, GameClient session, uint itemId) => Calls.Add(("off", itemId, -1));
        public void RollDice(Room room, GameClient session, FurnitureUseRequest request) => Calls.Add(("roll", request.ItemId, request.Parameter));
        public void UseOneWayGate(Room room, GameClient session, uint itemId) => Calls.Add(("gate", itemId, -1));
        public void UseWall(Room room, GameClient session, FurnitureUseRequest request) => Calls.Add(("wall", request.ItemId, request.Parameter));
        public void Use(Room room, GameClient session, FurnitureUseRequest request) => throw new NotSupportedException();
        public void Click(Room room, GameClient session, FurnitureClickRequest request) => throw new NotSupportedException();
    }
}
