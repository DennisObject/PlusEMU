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
        _clientManager = clientManager;
        _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (room.UsesV2Movement) return ParseExecutor(room, session, packet);
        var petId = packet.ReadInt();
        if (!room.GetRoomUserManager().TryGetPet(petId, out var pet))
        {
            //Check kick rights, just because it seems most appropriate.
            if (!room.CheckRights(session) && room.WhoCanKick != 2 && room.Group == null || room.Group != null && !room.CheckRights(session, false, true))
                return Task.CompletedTask;

            //Okay so, we've established we have no pets in this room by this virtual Id, let us check out users, maybe they're creeping as a pet?!
            var targetUser = session.GetHabbo().CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(petId);
            if (targetUser == null)
                return Task.CompletedTask;

            //Check some values first, please!
            if (targetUser.GetClient() == null || targetUser.GetClient().GetHabbo() == null)
                return Task.CompletedTask;

            //Update the targets PetId.
            targetUser.GetClient().GetHabbo().PetId = 0;

            //Quickly remove the old user instance.
            room.SendPacket(new UserRemoveComposer(targetUser.VirtualId));

            //Add the new one, they won't even notice a thing!!11 8-)
            room.SendUser(targetUser);
            return Task.CompletedTask;
        }
        if (session.GetHabbo().Id != pet.PetData.OwnerId && !room.CheckRights(session, true))
        {
            session.SendWhisper("You can only pickup your own pets, to kick a pet you must have room rights.");
            return Task.CompletedTask;
        }
        var data = pet.PetData;
        if (data == null || data.PetId <= 0 || !data.TrySaveRoom(_database, 0, 0, 0))
            return Task.CompletedTask;
        if (pet.RidingHorse)
        {
            var userRiding = room.GetRoomUserManager().GetRoomUserByVirtualId(pet.HorseId);
            if (userRiding != null)
            {
                userRiding.RidingHorse = false;
                userRiding.ApplyEffect(-1);
                userRiding.MoveTo(new(userRiding.X + 1, userRiding.Y + 1));
            }
            else
                pet.RidingHorse = false;
        }
        data.RoomId = 0;
        data.PlacedInRoom = false;
        data.DbState = PetDatabaseUpdateState.Updated;
        var owner = data.OwnerId == session.GetHabbo().Id ? session : _clientManager.GetClientByUserId(data.OwnerId);
        if (owner != null && owner.GetHabbo().Inventory.Pets.AddPet(data))
            owner.Send(new PetInventoryComposer(owner.GetHabbo().Inventory.Pets.Pets.Values.ToList()));

        room.GetRoomUserManager().RemoveBot(pet.VirtualId, false);
        return Task.CompletedTask;
    }
    private Task ParseExecutor(Room room, GameClient session, IIncomingPacket packet)
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
            room.GetGameMap().Navigation!.Mounts.DetachPickedUpHorse(pet);
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
        room.SendUser(user);
    }

    private void ReturnToInventory(GameClient session, Pet data)
    {
        data.RoomId = 0; data.PlacedInRoom = false; data.DbState = PetDatabaseUpdateState.Updated;
        var owner = data.OwnerId == session.GetHabbo().Id ? session : _clientManager.GetClientByUserId(data.OwnerId);
        if (owner != null && owner.GetHabbo().Inventory.Pets.AddPet(data))
            owner.Send(new PetInventoryComposer(owner.GetHabbo().Inventory.Pets.Pets.Values.ToList()));
    }
}
