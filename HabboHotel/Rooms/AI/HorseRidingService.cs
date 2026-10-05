using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;

namespace Plus.HabboHotel.Rooms.AI;

public interface IHorseRidingService
{
    void Ride(Room room, GameClient session, int petId, bool mount);
}

public sealed class HorseRidingService(IPetLocale petLocale) : IHorseRidingService
{
    public void Ride(Room room, GameClient session, int petId, bool mount)
    {
        // The packet's room is the one the user occupies now. A stale reference from before a move is ignored.
        var habbo = session.GetHabbo();
        if (!ReferenceEquals(habbo.CurrentRoom, room)) return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (user == null) return;
        if (!room.GetRoomUserManager().TryGetPet(petId, out var pet) || pet.PetData == null) return;
        if (!MayRide(session, user, pet)) return;
        if (room.UsesV2Movement) room.GetGameMap().Navigation!.Mounts.Ride(user, pet, mount, petLocale);
        else RideLegacy(room, session, user, pet, mount);
    }

    private static bool MayRide(GameClient session, RoomUser user, RoomUser pet)
    {
        if (pet.PetData.AnyoneCanRide != 0 || pet.PetData.OwnerId == user.UserId) return true;
        session.SendNotification(
            "You are unable to ride this horse.\nThe owner of the pet has not selected for anyone to ride it.");
        return false;
    }

    private void RideLegacy(Room room, GameClient session, RoomUser user, RoomUser pet, bool mount)
    {
        if (mount)
        {
            if (pet.RidingHorse)
            {
                var speech2 = petLocale.GetValue("pet.alreadymounted");
                pet.Chat(speech2[Random.Shared.Next(0, speech2.Length)]);
            }
            else if (user.RidingHorse)
                session.SendNotification("You are already riding a horse!");
            else
            {
                if (pet.Statusses.Count > 0)
                    pet.Statusses.Clear();
                var newX2 = user.X;
                var newY2 = user.Y;
                room.SendPacket(room.GetRoomItemHandler().UpdateUserOnRoller(pet, new(newX2, newY2), 0, room.GetGameMap().SqAbsoluteHeight(newX2, newY2)));
                room.SendPacket(room.GetRoomItemHandler().UpdateUserOnRoller(user, new(newX2, newY2), 0, room.GetGameMap().SqAbsoluteHeight(newX2, newY2) + 1));
                user.MoveTo(newX2, newY2);
                pet.ClearMovement(true);
                user.RidingHorse = true;
                pet.RidingHorse = true;
                pet.HorseId = user.VirtualId;
                user.HorseId = pet.VirtualId;
                user.ApplyEffect(77);
                user.RotBody = pet.RotBody;
                user.RotHead = pet.RotHead;
                user.UpdateNeeded = true;
                pet.UpdateNeeded = true;
            }
        }
        else
        {
            if (user.VirtualId == pet.HorseId)
            {
                pet.Statusses.Remove("sit");
                pet.Statusses.Remove("lay");
                pet.Statusses.Remove("snf");
                pet.Statusses.Remove("eat");
                pet.Statusses.Remove("ded");
                pet.Statusses.Remove("jmp");
                user.RidingHorse = false;
                user.HorseId = 0;
                pet.RidingHorse = false;
                pet.HorseId = 0;
                user.MoveTo(new(user.X + 2, user.Y + 2));
                user.ApplyEffect(-1);
                user.UpdateNeeded = true;
                pet.UpdateNeeded = true;
            }
            else
                session.SendNotification("Could not dismount this horse - You are not riding it!");
        }
        room.SendPacket(new PetHorseFigureInformationComposer(PetAppearanceSnapshots.Horse(pet)));
    }
}
