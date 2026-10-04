using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets.Horse;

internal class RideHorseEvent : RoomPacketEvent
{
    private readonly IPetLocale _petLocale;

    public RideHorseEvent(IPetLocale petLocale)
    {
        _petLocale = petLocale;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (room.UsesV2Movement) return ParseExecutor(room, session, packet);
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null)
            return Task.CompletedTask;
        var petId = packet.ReadInt();
        var type = packet.ReadBool();
        if (!room.GetRoomUserManager().TryGetPet(petId, out var pet))
            return Task.CompletedTask;
        if (pet.PetData == null)
            return Task.CompletedTask;
        if (pet.PetData.AnyoneCanRide == 0 && pet.PetData.OwnerId != user.UserId)
        {
            session.SendNotification(
                "You are unable to ride this horse.\nThe owner of the pet has not selected for anyone to ride it.");
            return Task.CompletedTask;
        }
        if (type)
        {
            if (pet.RidingHorse)
            {
                var speech2 = _petLocale.GetValue("pet.alreadymounted");
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
        room.SendPacket(new PetHorseFigureInformationComposer(pet));
        return Task.CompletedTask;
    }
    private Task ParseExecutor(Room room, GameClient session, IIncomingPacket packet)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null) return Task.CompletedTask;
        var petId = packet.ReadInt(); var mount = packet.ReadBool();
        if (!room.GetRoomUserManager().TryGetPet(petId, out var pet) || pet.PetData == null)
            return Task.CompletedTask;
        if (!MayRide(session, user, pet)) return Task.CompletedTask;
        room.GetGameMap().Navigation!.Mounts.Ride(user, pet, mount, _petLocale);
        return Task.CompletedTask;
    }

    private static bool MayRide(GameClient session, RoomUser user, RoomUser pet)
    {
        if (pet.PetData.AnyoneCanRide != 0 || pet.PetData.OwnerId == user.UserId) return true;
        session.SendNotification("You are unable to ride this horse.\nThe owner of the pet has not selected for anyone to ride it.");
        return false;
    }
}
