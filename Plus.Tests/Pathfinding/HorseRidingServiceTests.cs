using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Rooms.AI.Pets.Horse;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;
using Plus.HabboHotel.Users.Inventory.Pets;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void RideHandlerDecodesThePetIdAndMountThenOnlyDelegates()
    {
        var horses = new RideRecorder();

        new RideHorseEvent(horses).Parse(_room, _client, ClientPacket(50, true)).GetAwaiter().GetResult();

        Assert.Equal(new object[] { 50, true }, Assert.Single(horses.Calls));
    }

    [Fact]
    public void StaleRoomReferenceIsIgnored()
    {
        var rider = LegacyRider();
        var horse = LegacyHorse(2, 1);
        var before = _client.Sent.Count;

        new HorseRidingService(Locale()).Ride((Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)), _client, 50, true);

        Assert.False(rider.RidingHorse); Assert.False(horse.RidingHorse);
        Assert.Equal(before, _client.Sent.Count);
    }

    [Fact]
    public void MissingActorOrPetDoesNothing()
    {
        var horse = LegacyHorse(2, 1);
        new HorseRidingService(Locale()).Ride(_room, _client, 50, true);
        Assert.False(horse.RidingHorse);

        LegacyRider();
        new HorseRidingService(Locale()).Ride(_room, _client, 999, true);
        Assert.False(horse.RidingHorse);
    }

    [Fact]
    public void LegacyMountAndDismountFlipBothRidersAndPublishTheHorse()
    {
        var rider = LegacyRider();
        var horse = LegacyHorse(2, 1);
        var service = new HorseRidingService(Locale());

        service.Ride(_room, _client, 50, true);

        Assert.True(rider.RidingHorse); Assert.True(horse.RidingHorse);
        Assert.Equal(horse.VirtualId, rider.HorseId); Assert.Equal(rider.VirtualId, horse.HorseId);
        Assert.Contains(ServerPacketHeader.PetHorseFigureInformationComposer, _client.Sent);

        service.Ride(_room, _client, 50, false);

        Assert.False(rider.RidingHorse); Assert.False(horse.RidingHorse);
        Assert.Equal((0, 0), (rider.HorseId, horse.HorseId));
    }

    [Fact]
    public void LegacyOwnerOnlyPetRefusesAnotherRiderWithANotice()
    {
        var rider = LegacyRider();
        var horse = LegacyHorse(2, 1);
        horse.PetData.AnyoneCanRide = 0;
        horse.PetData.OwnerId = 99;
        var before = _client.Sent.Count;

        new HorseRidingService(Locale()).Ride(_room, _client, 50, true);

        Assert.False(rider.RidingHorse); Assert.False(horse.RidingHorse);
        Assert.True(_client.Sent.Count > before);
    }

    [Fact]
    public void LegacyOwnerMountsTheirOwnPetWhenAnyoneCanRideIsOff()
    {
        var rider = LegacyRider();
        var horse = LegacyHorse(2, 1);
        horse.PetData.AnyoneCanRide = 0;
        horse.PetData.OwnerId = rider.UserId;

        new HorseRidingService(Locale()).Ride(_room, _client, 50, true);

        Assert.True(rider.RidingHorse); Assert.True(horse.RidingHorse);
    }

    [Fact]
    public void V2OwnerOnlyPetRefusesAnotherRider()
    {
        var rider = ExecutorActor(0, 1);
        var horse = ExternalLifecycleHorse(2, 1); ExecutorTick();
        horse.PetData.AnyoneCanRide = 0;
        horse.PetData.OwnerId = 99;

        RideExternalHorse(horse, mount: true);
        ExecutorTick();

        Assert.False(rider.RidingHorse); Assert.False(horse.RidingHorse);
    }

    private IPetLocale Locale() => Proxy<IPetLocale>((_, _) => new[] { "horse" });

    private RoomUser LegacyRider()
    {
        var rider = new RoomUser(7, RoomId, 7, _room, _client, TestChatEmotions.Unused, new TestRewardProgress()) { X = 0, Y = 1, InternalRoomId = 7, UserId = 7 };
        Assert.True(LegacyUsers().TryAdd(7, rider));
        return rider;
    }

    private RoomUser LegacyHorse(int x, int y)
    {
        var horse = new RoomUser(0, RoomId, 2, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = x, Y = y, InternalRoomId = 2 };
        horse.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        horse.BotData.AiType = BotAiType.Pet;
        horse.PetData = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        var pet = horse.PetData;
        pet.PetId = 50; pet.VirtualId = horse.VirtualId; pet.OwnerId = 7; pet.RoomId = RoomId;
        pet.Name = "horse"; pet.OwnerName = "owner"; pet.Type = 13; pet.Race = "0"; pet.Color = "ffffff";
        pet.GnomeClothing = ""; pet.AnyoneCanRide = 1; pet.Saddle = 1; pet.Energy = pet.Nutrition = 100;
        pet.ExperienceLevels = [100, 200, 400, 600]; pet.PlacedInRoom = true;
        pet.Attach(_room, TestGameClientManager.Empty, TestRewardProgress.Unused);
        Assert.True(LegacyPets().TryAdd(pet.PetId, horse));
        return horse;
    }

    private ConcurrentDictionary<int, RoomUser> LegacyUsers() =>
        (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;

    private ConcurrentDictionary<int, RoomUser> LegacyPets() =>
        (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_pets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;

    private sealed class RideRecorder : IHorseRidingService
    {
        public List<object[]> Calls { get; } = new();
        public void Ride(Room room, Plus.HabboHotel.GameClients.GameClient session, int petId, bool mount) => Calls.Add(new object[] { petId, mount });
    }
}
