using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.Rooms.AI.Pets;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Pets;
using Xunit;

namespace Plus.Tests;

public sealed class PetPlacementDatabaseFactAttribute : FactAttribute
{
    public PetPlacementDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE") is null)
            Skip = "Opt-in isolated pet placement MariaDB probe.";
    }
}

public sealed class PetPlacementServiceTests
{
    [Fact]
    public async Task HandlersDecodeExactPrimitiveFields()
    {
        var pets = new RecordingService();
        var room = Room();

        await new PlacePetEvent(pets).Parse(room, null!, HabbiconTestSupport.Incoming(12, 3, 4));
        await new PickUpPetEvent(pets).Parse(room, null!, HabbiconTestSupport.Incoming(13));

        Assert.Equal((room, 12, 3, 4), pets.Placed);
        Assert.Equal((room, 13), pets.PickedUp);
    }

    [Fact]
    public void WrongCurrentRoomAndWrongCanonicalOwnerAreDenied()
    {
        var world = World(ownerId: 7, actorId: 8);
        world.Client.GetHabbo().CurrentRoom = null;

        world.Service.Place(world.Room, world.Client, world.Pet.PetId, 1, 1);
        world.Client.GetHabbo().CurrentRoom = world.Room;
        world.Service.Place(world.Room, world.Client, world.Pet.PetId, 1, 1);

        Assert.Empty(world.Store.Moves);
        Assert.Contains(world.Pet.PetId, world.Client.GetHabbo().Inventory.Pets.Pets.Keys);
        Assert.False(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out _));
    }

    [Fact]
    public void PlacementPersistsBeforeRuntimeAndInventoryPublication()
    {
        var world = World();
        world.Store.BeforeMove = move =>
        {
            Assert.Equal((world.Pet.PetId, 7, 0u, 42u, 1, 1),
                (move.PetId, move.OwnerId, move.PreviousRoomId, move.RoomId, move.X, move.Y));
            Assert.Contains(world.Pet.PetId, world.Client.GetHabbo().Inventory.Pets.Pets.Keys);
            Assert.False(world.Pet.PlacedInRoom);
            Assert.False(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out _));
        };

        world.Service.Place(world.Room, world.Client, world.Pet.PetId, 1, 1);

        Assert.DoesNotContain(world.Pet.PetId, world.Client.GetHabbo().Inventory.Pets.Pets.Keys);
        Assert.True(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out var placed));
        Assert.Same(world.Pet, placed.PetData);
        Assert.Equal((42u, 1, 1, true), (world.Pet.RoomId, world.Pet.X, world.Pet.Y, world.Pet.PlacedInRoom));
        Assert.Single(world.Packets);
    }

    [Fact]
    public void PlacementPersistenceFailureLeavesInventoryAndRoomUnchanged()
    {
        var world = World();
        world.Store.Result = false;

        world.Service.Place(world.Room, world.Client, world.Pet.PetId, 1, 1);

        Assert.Contains(world.Pet.PetId, world.Client.GetHabbo().Inventory.Pets.Pets.Keys);
        Assert.False(world.Pet.PlacedInRoom);
        Assert.Equal(0u, world.Pet.RoomId);
        Assert.False(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out _));
        Assert.Empty(world.Packets);
    }

    [Fact]
    public void PickupFailureLeavesPlacedPetOutOfInventory()
    {
        var world = World();
        world.Service.Place(world.Room, world.Client, world.Pet.PetId, 1, 1);
        world.Packets.Clear();
        world.Store.Result = false;

        world.Service.PickUp(world.Room, world.Client, world.Pet.PetId);

        Assert.True(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out _));
        Assert.DoesNotContain(world.Pet.PetId, world.Client.GetHabbo().Inventory.Pets.Pets.Keys);
        Assert.Equal((42u, true), (world.Pet.RoomId, world.Pet.PlacedInRoom));
        Assert.Empty(world.Packets);
    }

    [Fact]
    public void NonOwnerWithoutRoomOwnershipCannotPickUpPet()
    {
        var world = World();
        world.Service.Place(world.Room, world.Client, world.Pet.PetId, 1, 1);
        var (intruder, _) = HabbiconTestSupport.Client(new Habbo
        {
            Id = 8,
            Username = "intruder",
            CurrentRoom = world.Room,
            Inventory = new InventoryComponent { Pets = new([]) },
        });
        var writesBefore = world.Store.Moves.Count;

        world.Service.PickUp(world.Room, intruder, world.Pet.PetId);

        Assert.Equal(writesBefore, world.Store.Moves.Count);
        Assert.True(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out _));
        Assert.DoesNotContain(world.Pet.PetId, intruder.GetHabbo().Inventory.Pets.Pets.Keys);
    }

    [Fact]
    public void PickupPersistsBeforeReturningPetToOwnerInventory()
    {
        var world = World();
        world.Service.Place(world.Room, world.Client, world.Pet.PetId, 1, 1);
        world.Packets.Clear();
        world.Store.BeforeMove = move =>
        {
            if (move.RoomId != 0)
                return;
            Assert.Equal(42u, move.PreviousRoomId);
            Assert.True(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out _));
            Assert.DoesNotContain(world.Pet.PetId, world.Client.GetHabbo().Inventory.Pets.Pets.Keys);
        };

        world.Service.PickUp(world.Room, world.Client, world.Pet.PetId);

        Assert.False(world.Room.GetRoomUserManager().TryGetPet(world.Pet.PetId, out _));
        Assert.Contains(world.Pet.PetId, world.Client.GetHabbo().Inventory.Pets.Pets.Keys);
        Assert.Equal((0u, false), (world.Pet.RoomId, world.Pet.PlacedInRoom));
        Assert.Single(world.Packets);
    }

    [PetPlacementDatabaseFact]
    public void StoreCommitsBothRowsAndRollsBackPartialMoves()
    {
        var rootBuilder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"))
        {
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true,
        };
        using var root = new MySqlConnection(rootBuilder.ConnectionString);
        root.Open();
        var schema = "task_refactor_tests_pets_" + Guid.NewGuid().ToString("N");
        root.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var builder = new MySqlConnectionStringBuilder(rootBuilder.ConnectionString) { Database = schema };
            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE bots (
                    id INT PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL,
                    x INT NOT NULL, y INT NOT NULL, z DOUBLE NOT NULL, ai_type VARCHAR(20) NOT NULL);
                CREATE TABLE bots_petdata (
                    id INT PRIMARY KEY, experience INT NOT NULL, energy INT NOT NULL,
                    nutrition INT NOT NULL, respect INT NOT NULL);
                INSERT INTO bots VALUES (12, 7, 0, 0, 0, 0, 'pet');
                INSERT INTO bots_petdata VALUES (12, 1, 2, 3, 4);
                """);
            var store = new PetRoomStore(new ProbeDatabase(builder.ConnectionString), TestLogging.For<PetRoomStore>());

            Assert.True(store.TryMove(new(12, 7, 0, 42, 3, 4, 1.25, 10, 20, 30, 40)));
            Assert.Equal((42u, 3, 4, 1.25), connection.QuerySingle<(uint, int, int, double)>(
                "SELECT room_id, x, y, z FROM bots WHERE id = 12"));
            Assert.Equal((10, 20, 30, 40), connection.QuerySingle<(int, int, int, int)>(
                "SELECT experience, energy, nutrition, respect FROM bots_petdata WHERE id = 12"));

            connection.Execute("DELETE FROM bots_petdata WHERE id = 12");
            Assert.False(store.TryMove(new(12, 7, 42, 0, 0, 0, 0, 10, 20, 30, 40)));
            Assert.Equal(42u, connection.ExecuteScalar<uint>("SELECT room_id FROM bots WHERE id = 12"));
            Assert.False(store.TryMove(new(12, 99, 42, 0, 0, 0, 0, 10, 20, 30, 40)));
            Assert.Equal(42u, connection.ExecuteScalar<uint>("SELECT room_id FROM bots WHERE id = 12"));
        }
        finally
        {
            root.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private static WorldFixture World(int ownerId = 7, int actorId = 7)
    {
        var room = Room();
        var pet = Pet(ownerId);
        var inventory = new PetsInventoryComponent([pet]);
        var habbo = new Habbo
        {
            Id = actorId,
            Username = $"user{actorId}",
            CurrentRoom = room,
            Inventory = new InventoryComponent { Pets = inventory },
        };
        var (client, packets) = HabbiconTestSupport.Client(habbo);
        var store = new RecordingStore();
        var clients = CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, arguments) =>
            method == nameof(IGameClientManager.GetClientByUserId) && (int)arguments![0]! == actorId
                ? client
                : null);
        var settings = CatalogSnapshotTestSupport.Proxy<Plus.Core.Settings.ISettingsManager>((method, arguments) =>
            method == "TryGetValue" && (string)arguments![0]! == "room.pets.placement_limit" ? "5" : null);
        return new(room, pet, client, packets, store, new(store, clients, settings));
    }

    private static Room Room()
    {
        var model = new RoomModel("pets-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, false);
        var room = new Room(new RoomData { Id = 42, AllowPets = true, Model = model }, [],
            TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused);
        Set(room, "_gamemap", new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        Set(room, "_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty));
        Set(room, "_userSnapshots", CatalogSnapshotTestSupport.Proxy<IRoomUserSnapshotService>((_, _) => null));
        return room;
    }

    private static Pet Pet(int ownerId)
    {
        var pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.PetId = 12;
        pet.OwnerId = ownerId;
        pet.RoomId = 0;
        pet.Name = "Pixel";
        pet.OwnerName = "owner";
        pet.Type = 1;
        pet.Race = "0";
        pet.Color = "ffffff";
        pet.GnomeClothing = "";
        pet.ExperienceLevels = [100];
        pet.Energy = 20;
        pet.Nutrition = 30;
        pet.Respect = 4;
        return pet;
    }

    private static void Set(Room room, string field, object value) =>
        typeof(Room).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, value);

    private sealed record WorldFixture(
        Room Room,
        Pet Pet,
        GameClient Client,
        List<(uint Header, byte[] Payload)> Packets,
        RecordingStore Store,
        PetPlacementService Service);

    private sealed class RecordingService : IPetPlacementService
    {
        public (Room Room, int PetId, int X, int Y)? Placed { get; private set; }
        public (Room Room, int PetId)? PickedUp { get; private set; }
        public void Place(Room room, GameClient session, int petId, int x, int y) => Placed = (room, petId, x, y);
        public void PickUp(Room room, GameClient session, int petId) => PickedUp = (room, petId);
    }

    private sealed class RecordingStore : IPetRoomStore
    {
        public bool Result { get; set; } = true;
        public Action<PetRoomMove>? BeforeMove { get; set; }
        public List<PetRoomMove> Moves { get; } = [];
        public bool TryMove(PetRoomMove move)
        {
            BeforeMove?.Invoke(move);
            Moves.Add(move);
            return Result;
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
