using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms.AI;

[Singleton]
public interface IPetPlacementService
{
    void Place(Room room, GameClient session, int petId, int x, int y);
    void PickUp(Room room, GameClient session, int petId);
}

public sealed class PetPlacementService(
    IPetRoomStore store,
    IGameClientManager clients,
    ISettingsManager settings) : IPetPlacementService
{
    public void Place(Room room, GameClient session, int petId, int x, int y)
    {
        var habbo = session.GetHabbo();
        if (!ReferenceEquals(habbo.CurrentRoom, room))
            return;

        if (!room.AllowPets && !room.CheckRights(session, true))
        {
            session.Send(new RoomErrorNotifComposer(PetPlacementError.RoomDisallowsPets));
            return;
        }

        if (room.GetRoomUserManager().PetCount >= Convert.ToInt32(settings.TryGetValue("room.pets.placement_limit")))
        {
            session.Send(new RoomErrorNotifComposer(PetPlacementError.RoomLimitReached));
            return;
        }

        if (!habbo.Inventory.Pets.Pets.TryGetValue(petId, out var pet)
            || pet.PetId <= 0
            || pet.OwnerId != habbo.Id)
            return;

        lock (pet)
        {
            if (pet.PlacedInRoom)
            {
                session.SendNotification("This pet is already in the room?");
                return;
            }

            if (!room.GetGameMap().ValidTile(x, y)
                || !room.GetGameMap().SquareIsOpen(x, y, false)
                || !room.GetGameMap().CanWalk(x, y, false))
            {
                session.Send(new RoomErrorNotifComposer(PetPlacementError.InvalidTile));
                return;
            }

            var z = room.GetGameMap().SqAbsoluteHeight(x, y);
            if (!store.TryMove(Move(pet, room.RoomId, x, y, z)))
                return;

            if (room.GetRoomUserManager().TryGetPet(pet.PetId, out var oldPet))
                room.GetRoomUserManager().RemoveBot(oldPet.VirtualId, false);

            pet.X = x;
            pet.Y = y;
            pet.Z = z;
            pet.PlacedInRoom = true;
            pet.RoomId = room.RoomId;
            var speeches = new List<RandomSpeech>();
            var roomBot = new RoomBot(pet.PetId, pet.RoomId, "pet", "freeroam", pet.Name, "", pet.Look,
                x, y, z, 0, 0, 0, 0, 0, ref speeches, "", 0, pet.OwnerId, false, 0, false, 0);
            room.GetRoomUserManager().DeployBot(roomBot, pet);
            pet.DbState = PetDatabaseUpdateState.Updated;
            habbo.Inventory.Pets.RemovePet(pet.PetId);
            session.Send(new PetInventoryComposer(PetAppearanceSnapshots.Inventory(habbo.Inventory.Pets.Pets.Values)));
        }
    }

    public void PickUp(Room room, GameClient session, int petId)
    {
        var habbo = session.GetHabbo();
        if (!ReferenceEquals(habbo.CurrentRoom, room))
            return;

        if (!room.GetRoomUserManager().TryGetPet(petId, out var pet))
        {
            RestoreAvatarPet(room, session, petId);
            return;
        }

        var data = pet.PetData;
        if (data == null || data.PetId <= 0 || data.RoomId != room.RoomId)
            return;
        if (habbo.Id != data.OwnerId && !room.CheckRights(session, true))
        {
            session.SendWhisper("You can only pickup your own pets, to kick a pet you must have room rights.");
            return;
        }

        lock (data)
        {
            if (data.RoomId != room.RoomId || !data.PlacedInRoom || !store.TryMove(Move(data, 0, 0, 0, 0)))
                return;

            DetachRider(room, pet);
            data.RoomId = 0;
            data.PlacedInRoom = false;
            data.DbState = PetDatabaseUpdateState.Updated;
            var owner = data.OwnerId == habbo.Id ? session : clients.GetClientByUserId(data.OwnerId);
            if (owner?.GetHabbo() is { } ownerHabbo && ownerHabbo.Inventory.Pets.AddPet(data))
                owner.Send(new PetInventoryComposer(PetAppearanceSnapshots.Inventory(ownerHabbo.Inventory.Pets.Pets.Values)));

            room.GetRoomUserManager().RemoveBot(pet.VirtualId, false);
        }
    }

    private static PetRoomMove Move(Pet pet, uint roomId, int x, int y, double z) => new(
        pet.PetId, pet.OwnerId, pet.RoomId, roomId, x, y, z,
        pet.Experience, pet.Energy, pet.Nutrition, pet.Respect);

    private static void DetachRider(Room room, RoomUser pet)
    {
        if (!pet.RidingHorse)
            return;

        if (room.UsesV2Movement)
        {
            room.GetGameMap().Navigation!.Mounts.DetachPickedUpHorse(pet);
            return;
        }

        var rider = room.GetRoomUserManager().GetRoomUserByVirtualId(pet.HorseId);
        if (rider == null)
        {
            pet.RidingHorse = false;
            return;
        }

        rider.RidingHorse = false;
        rider.ApplyEffect(-1);
        rider.MoveTo(new(rider.X + 1, rider.Y + 1));
    }

    private static void RestoreAvatarPet(Room room, GameClient session, int petId)
    {
        if (!room.CheckRights(session) && room.WhoCanKick != 2 && room.Group == null
            || room.Group != null && !room.CheckRights(session, false, true))
            return;

        var user = room.GetRoomUserManager().GetRoomUserByHabbo(petId);
        var userHabbo = user?.GetClient()?.GetHabbo();
        if (user == null || userHabbo == null)
            return;

        userHabbo.PetId = 0;
        room.SendPacket(new UserRemoveComposer(user.VirtualId));
        room.SendUser(user);
    }
}
