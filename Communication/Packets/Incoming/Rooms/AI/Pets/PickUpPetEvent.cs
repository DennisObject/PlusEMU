using Plus.HabboHotel.Rooms.AI;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets;

internal class PickUpPetEvent : RoomPacketEvent
{
    private readonly IGameClientManager _clientManager;
    private readonly IDatabase _database;

    public PickUpPetEvent(IGameClientManager clientManager, IDatabase database)
    {
        _clientManager = clientManager; _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var petId = packet.ReadInt();
        if (!room.GetRoomUserManager().TryGetPet(petId, out var pet))
        {
            RestoreAvatarPet(room, session, petId);
            return Task.CompletedTask;
        }
        if (!MayPickup(room, session, pet)) return Task.CompletedTask;
        var data = pet.PetData;
        if (data == null || data.PetId <= 0 || !data.TrySaveRoom(_database, 0, 0, 0)) return Task.CompletedTask;
        if (pet.RidingHorse)
        {
            if (room.GetGameMap().Navigation is { UsesExecutor: true } navigation)
                navigation.Mounts.DetachPickedUpHorse(pet);
            else DetachRiderLegacy(room, pet);
        }
        ReturnToInventory(session, data);
        room.GetRoomUserManager().RemoveBot(pet.VirtualId, false);
        return Task.CompletedTask;
    }

    private static bool MayPickup(Room room, GameClient session, RoomUser pet)
    {
        if (session.GetHabbo().Id == pet.PetData.OwnerId || room.CheckRights(session, true)) return true;
        session.SendWhisper("You can only pickup your own pets, to kick a pet you must have room rights.");
        return false;
    }

    private static void RestoreAvatarPet(Room room, GameClient session, int petId)
    {
        if (!room.CheckRights(session) && room.WhoCanKick != 2 && room.Group == null
            || room.Group != null && !room.CheckRights(session, false, true)) return;
        var user = session.GetHabbo().CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(petId);
        if (user?.GetClient()?.GetHabbo() == null) return;
        user.GetClient().GetHabbo().PetId = 0;
        room.SendPacket(new UserRemoveComposer(user.VirtualId));
        room.SendPacket(new UsersComposer(user));
    }

    private static void DetachRiderLegacy(Room room, RoomUser pet)
    {
        var rider = room.GetRoomUserManager().GetRoomUserByVirtualId(pet.HorseId);
        if (rider == null) { pet.RidingHorse = false; return; }
        rider.RidingHorse = false; rider.ApplyEffect(-1);
        rider.MoveTo(new(rider.X + 1, rider.Y + 1));
    }

    private void ReturnToInventory(GameClient session, Pet data)
    {
        data.RoomId = 0; data.PlacedInRoom = false; data.DbState = PetDatabaseUpdateState.Updated;
        var owner = data.OwnerId == session.GetHabbo().Id ? session : _clientManager.GetClientByUserId(data.OwnerId);
        if (owner != null && owner.GetHabbo().Inventory.Pets.AddPet(data))
            owner.Send(new PetInventoryComposer(owner.GetHabbo().Inventory.Pets.Pets.Values.ToList()));
    }
}
