using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.AI.Pets;
using Plus.Communication.Packets.Incoming.Rooms.FloorPlan;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task FloorPlanAndTrainingHandlersOnlyDecodeTheirPrimitivesAndDelegate()
    {
        var floors = new FloorPresentationRecorder();
        var pets = new PetPresentationRecorder();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        await new GetRoomEntryTileEvent(floors).Parse(client, ClientPacket());
        await new GetOccupiedTilesEvent(floors).Parse(client, ClientPacket());
        await new GetPetTrainingPanelEvent(pets).Parse(client, ClientPacket(50));
        Assert.Equal(new (GameClient, string)[] { (client, "entry"), (client, "occupied") }, floors.Calls);
        Assert.Equal((client, 50), Assert.Single(pets.Calls));
        Assert.Empty(sent);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new GetPetTrainingPanelEvent(pets).Parse(client, ClientPacket()));
        Assert.Single(pets.Calls);
    }

    [Fact]
    public void FloorPlanPresentationCapturesTheDoorAndWholeRotatedFurnitureFootprint()
    {
        var model = _room.GetGameMap().Model;
        model.DoorX = 2;
        model.DoorY = 3;
        model.DoorOrientation = 6;
        var sofa = Furni(10, InteractionType.None, WiredBoxType.None);
        sofa.Definition.Width = 1;
        sofa.Definition.Length = 2;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, sofa, 1, 1, 2, true, false, false));
        _client.Sent.Clear();
        _client.Packets.Clear();
        // These read requests have always been available to room visitors, without owner rights.
        _client.GetHabbo().Id = 99;
        var service = new FloorPlanUpdateService(null!, null!);
        service.ShowEntryTile(_client);
        service.ShowOccupiedTiles(_client);
        Assert.Equal(new[] { ServerPacketHeader.RoomEntryTileComposer, ServerPacketHeader.RoomOccupiedTilesComposer }, _client.Sent);
        var entry = new FlashIncomingPacket { Buffer = _client.Packets[0].Body };
        Assert.Equal((2, 3, 6), (entry.ReadInt(), entry.ReadInt(), entry.ReadInt()));
        var occupied = new FlashIncomingPacket { Buffer = _client.Packets[1].Body };
        Assert.Equal(2, occupied.ReadInt());
        Assert.Equal((1, 1), (occupied.ReadInt(), occupied.ReadInt()));
        Assert.Equal((2, 1), (occupied.ReadInt(), occupied.ReadInt()));
    }

    [Fact]
    public void FloorPlanAndPetTrainingRequestsOutsideARoomPublishNothing()
    {
        _client.GetHabbo().CurrentRoom = null;
        var floors = new FloorPlanUpdateService(null!, null!);
        floors.ShowEntryTile(_client);
        floors.ShowOccupiedTiles(_client);
        new PetInformationService(_interactionClock).SendTrainingPanel(_client, 50);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void PetTrainingUsesCapturedPetIdAndLevelAndRefusesMissingDataOrForeignRoom()
    {
        var service = new PetInformationService(_interactionClock);
        service.SendTrainingPanel(_client, 50);
        Assert.Empty(_client.Sent);
        var horse = LegacyHorse(2, 1);
        horse.PetData.Experience = 300;
        service.SendTrainingPanel(_client, 50);
        Assert.Equal(ServerPacketHeader.PetTrainingPanelComposer, Assert.Single(_client.Sent));
        var packet = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
        var values = Enumerable.Range(0, 19).Select(_ => packet.ReadInt()).ToArray();
        Assert.Equal(new[] { 50, 8, 46, 0, 1, 2, 3, 4, 5, 6, 2, 46, 0, 1, 2, 3, 4, 5, 6 }, values);
        _client.Sent.Clear();
        _client.Packets.Clear();
        var foreign = new RoomUser(0, 99, 2, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { BotData = horse.BotData, PetData = horse.PetData };
        LegacyPets()[50] = foreign;
        service.SendTrainingPanel(_client, 50);
        horse.PetData = null!;
        LegacyPets()[50] = horse;
        service.SendTrainingPanel(_client, 50);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void AccountPetTrainingKeepsTheExistingWhisperFallback()
    {
        LegacyRider();
        new PetInformationService(_interactionClock).SendTrainingPanel(_client, 7);
        Assert.Equal(ServerPacketHeader.WhisperComposer, Assert.Single(_client.Sent));
        var packet = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
        packet.ReadInt();
        Assert.Equal("Maybe one day, boo boo.", packet.ReadString());
    }

    private sealed class FloorPresentationRecorder : IFloorPlanUpdateService
    {
        public List<(GameClient, string)> Calls { get; } = new();
        public void ShowEntryTile(GameClient session) => Calls.Add((session, "entry"));
        public void ShowOccupiedTiles(GameClient session) => Calls.Add((session, "occupied"));
        public void Update(Room room, GameClient session, FloorPlanUpdateRequest body) => throw new NotSupportedException();
    }
    private sealed class PetPresentationRecorder : IPetInformationService
    {
        public List<(GameClient, int)> Calls { get; } = new();
        public void SendTrainingPanel(GameClient session, int petId) => Calls.Add((session, petId));
        public void SendInformation(GameClient session, int petId) => throw new NotSupportedException();
        public PetInformationSnapshot Capture(Pet pet) => throw new NotSupportedException();
    }
}
