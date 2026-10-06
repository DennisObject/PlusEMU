using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class HorseCustomizationServiceTests
{
    [Fact]
    public void OwnerCanToggleRidingAfterPersistence()
    {
        var (room, pet) = Horse(ownerId: 7, anyoneCanRide: 0);
        var (session, _) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var store = new RecordingStore();
        var service = new HorseCustomizationService(null!, null!, null!, store, new PetInformationService(TimeProvider.System));

        service.ToggleRiding(room, session, pet.PetId);

        var update = Assert.Single(store.Updates);
        Assert.Equal(pet.PetId, update.PetId);
        Assert.Equal("anyone_ride", update.Column);
        Assert.Equal(1, update.Value);
        Assert.Equal(1, pet.AnyoneCanRide);
    }

    [Fact]
    public void NonOwnerCannotToggleRiding()
    {
        var (room, pet) = Horse(ownerId: 7, anyoneCanRide: 0);
        var (session, _) = HabbiconTestSupport.Client(new Habbo { Id = 8 });
        var store = new RecordingStore();
        var service = new HorseCustomizationService(null!, null!, null!, store, new PetInformationService(TimeProvider.System));

        service.ToggleRiding(room, session, pet.PetId);

        Assert.Empty(store.Updates);
        Assert.Equal(0, pet.AnyoneCanRide);
    }

    [Fact]
    public void PersistenceFailureDoesNotPublishRidingChange()
    {
        var (room, pet) = Horse(ownerId: 7, anyoneCanRide: 0);
        var (session, _) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var service = new HorseCustomizationService(null!, null!, null!, new RecordingStore { Fail = true }, new PetInformationService(TimeProvider.System));

        Assert.Throws<InvalidOperationException>(() => service.ToggleRiding(room, session, pet.PetId));
        Assert.Equal(0, pet.AnyoneCanRide);
    }

    private static (Room Room, Pet Pet) Horse(int ownerId, int anyoneCanRide)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, manager);
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.PetId = 12;
        pet.Name = "horse";
        pet.OwnerName = "owner";
        pet.ExperienceLevels = [100];
        pet.OwnerId = ownerId;
        pet.AnyoneCanRide = anyoneCanRide;
        var roomUser = new RoomUser(0, room.Id, 3, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { PetData = pet };
        var pets = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_pets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        pets[pet.PetId] = roomUser;

        return (room, pet);
    }

    private sealed class RecordingStore : IHorseCustomizationStore
    {
        public bool Fail { get; init; }
        public List<(int PetId, string Column, object Value)> Updates { get; } = [];
        public void UpdatePet(int petId, string column, object value)
        {
            if (Fail) {
                throw new InvalidOperationException("forced failure");
            }

            Updates.Add((petId, column, value));
        }
        public void ConsumeItem(int petId, string column, object value, uint itemId, uint roomId, int ownerId)
        {
            if (Fail) {
                throw new InvalidOperationException("forced failure");
            }
        }
    }
}
