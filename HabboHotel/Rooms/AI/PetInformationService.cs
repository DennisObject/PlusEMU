using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.AI;

public sealed record PetInformationSnapshot(int Id, string Name, int Level, int MaxLevel, int Experience,
    int ExperienceGoal, int Energy, int MaxEnergy, int Nutrition, int MaxNutrition, int Respect, int OwnerId,
    int AgeInDays, string OwnerName, bool HasSaddle, int AnyoneCanRide);

public interface IPetInformationService
{
    void SendInformation(GameClient session, int petId);
    PetInformationSnapshot Capture(Pet pet);
}

public sealed class PetInformationService(TimeProvider clock) : IPetInformationService
{
    public void SendInformation(GameClient session, int petId)
    {
        var habbo = session.GetHabbo();
        if (!habbo.InRoom || habbo.CurrentRoom == null) return;
        var room = habbo.CurrentRoom;
        if (room.GetRoomUserManager().TryGetPet(petId, out var pet))
        {
            if (pet.RoomId != room.RoomId || pet.PetData == null) return;
            session.Send(new PetInformationComposer(Capture(pet.PetData)));
            return;
        }
        var target = room.GetRoomUserManager().GetRoomUserByHabbo(petId)?.GetClient()?.GetHabbo();
        if (target != null) session.Send(new PetInformationComposer(Capture(target, clock.GetUtcNow())));
    }

    public PetInformationSnapshot Capture(Pet pet)
    {
        var age = Math.Floor((clock.GetUtcNow().ToUnixTimeSeconds() - pet.CreationStamp) / 86400);
        var days = double.IsNaN(age) ? 0 : (int)Math.Clamp(age, 0, int.MaxValue);
        return new(pet.PetId, pet.Name, pet.Level, Pet.MaxLevel, pet.Experience, pet.ExperienceGoal,
            pet.Energy, Pet.MaxEnergy, pet.Nutrition, Pet.MaxNutrition, pet.Respect, pet.OwnerId,
            days, pet.OwnerName, pet.Saddle > 0, pet.AnyoneCanRide);
    }

    internal static PetInformationSnapshot Capture(Habbo habbo, DateTimeOffset now)
    {
        var created = habbo.AccountCreatedAt?.ToUnixTimeSeconds() ?? now.ToUnixTimeSeconds();
        var age = (int)Math.Clamp((now.ToUnixTimeSeconds() - created) / 86400, 0, int.MaxValue);
        return new(habbo.Id, habbo.Username, habbo.Access.SecurityLevel, 10, 0, 0, 100, 100, 100, 100,
            habbo.HabboStats.Respect, habbo.Id, age, habbo.Username, false, 0);
    }
}
