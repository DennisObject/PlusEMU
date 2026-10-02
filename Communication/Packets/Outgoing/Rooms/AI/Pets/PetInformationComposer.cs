using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;

public class PetInformationComposer : IServerPacket
{
    private readonly Habbo? _habbo;
    private readonly Pet? _pet;

    public uint MessageId => ServerPacketHeader.PetInformationComposer;

    public PetInformationComposer(Pet pet)
    {
        _pet = pet;
    }

    public void Compose(IOutgoingPacket packet)
    {
        if (_pet != null)
        {
            if (!PlusEnvironment.Game.RoomManager.TryGetRoom(_pet.RoomId, out var room))
                return;
            packet.WriteInteger(_pet.PetId);
            packet.WriteString(_pet.Name);
            packet.WriteInteger(_pet.Level);
            packet.WriteInteger(Pet.MaxLevel);
            packet.WriteInteger(_pet.Experience);
            packet.WriteInteger(_pet.ExperienceGoal);
            packet.WriteInteger(_pet.Energy);
            packet.WriteInteger(Pet.MaxEnergy);
            packet.WriteInteger(_pet.Nutrition);
            packet.WriteInteger(Pet.MaxNutrition);
            packet.WriteInteger(_pet.Respect);
            packet.WriteInteger(_pet.OwnerId);
            packet.WriteInteger(_pet.Age);
            packet.WriteString(_pet.OwnerName);
            packet.WriteInteger(1); //3 on hab
            packet.WriteBoolean(_pet.Saddle > 0);
            packet.WriteBoolean(false);
            WriteStatus(packet, _pet.AnyoneCanRide);
        }
        else if (_habbo != null)
        {
            packet.WriteInteger(_habbo.Id);
            packet.WriteString(_habbo.Username);
            packet.WriteInteger(_habbo.Rank);
            packet.WriteInteger(10);
            packet.WriteInteger(0);
            packet.WriteInteger(0);
            packet.WriteInteger(100);
            packet.WriteInteger(100);
            packet.WriteInteger(100);
            packet.WriteInteger(100);
            packet.WriteInteger(_habbo.HabboStats.Respect);
            packet.WriteInteger(_habbo.Id);
            packet.WriteInteger(Convert.ToInt32(Math.Floor((UnixTimestamp.GetNow() - _habbo.AccountCreated) / 86400))); //How?
            packet.WriteString(_habbo.Username);
            packet.WriteInteger(1); //3 on hab
            packet.WriteBoolean(false);
            packet.WriteBoolean(false);
            WriteStatus(packet, 0);
        }
    }

    private static void WriteStatus(IOutgoingPacket packet, int publiclyRideable)
    {
        packet.WriteInteger(0); // Skill threshold count.
        packet.WriteInteger(publiclyRideable);
        packet.WriteBoolean(false); // Breedable.
        packet.WriteBoolean(true); // Fully grown.
        packet.WriteBoolean(false); // Dead.
        packet.WriteInteger(0); // Unknown rarity.
        packet.WriteInteger(-1); // Maximum time to live.
        packet.WriteInteger(-1); // Remaining time to live.
        packet.WriteInteger(-1); // Remaining grow time.
        packet.WriteBoolean(false); // Publicly breedable.
    }

    public PetInformationComposer(Habbo habbo)
    {
        _habbo = habbo;
    }
}