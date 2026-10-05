using Plus.HabboHotel.Rooms.AI;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class KickPetsCommand : IChatCommand
{
    private readonly IGameClientManager _gameClientManager;
    private readonly IDatabase _database;
    public string Key => "kickpets";

    public string Parameters => "";

    public string Description => "Kick all of the pets from the room.";

    public KickPetsCommand(IGameClientManager gameClientManager, IDatabase database)
    {
        _gameClientManager = gameClientManager;
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!room.CheckRights(session, true))
        {
            session.SendWhisper("Oops, only the room owner can run this command!");
            return;
        }
        if (room.GetRoomUserManager().GetPets().Count == 0) session.SendWhisper("Oops, there isn't any pets in here!?");
        foreach (var bot in room.GetRoomUserManager().GetUserList().ToList())
        {
            if (bot?.PetData == null)
                continue;
            var pet = bot.PetData;
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute("UPDATE bots SET room_id=0,x=0,y=0,z=0 WHERE id=@petId LIMIT 1", new { petId = pet.PetId }, transaction);
            connection.Execute("UPDATE bots_petdata SET experience=@experience,energy=@energy,nutrition=@nutrition,respect=@respect WHERE id=@petId LIMIT 1",
                new { pet.Experience, pet.Energy, pet.Nutrition, pet.Respect, petId = pet.PetId }, transaction);
            transaction.Commit();

            if (bot.RidingHorse)
            {
                var rider = room.GetRoomUserManager().GetRoomUserByVirtualId(bot.HorseId);
                if (rider != null)
                {
                    rider.RidingHorse = false;
                    rider.ApplyEffect(-1);
                    rider.MoveTo(new(rider.X + 1, rider.Y + 1));
                }
                else
                    bot.RidingHorse = false;
            }
            pet.RoomId = 0;
            pet.PlacedInRoom = false;
            room.GetRoomUserManager().RemoveBot(bot.VirtualId, false);
            var ownerClient = _gameClientManager.GetClientByUserId(pet.OwnerId);
            if (ownerClient?.GetHabbo() != null && ownerClient.GetHabbo().Inventory.Pets.AddPet(pet))
                ownerClient.Send(new PetInventoryComposer(PetAppearanceSnapshots.Inventory(ownerClient.GetHabbo().Inventory.Pets.Pets.Values.ToList())));
        }
        session.SendWhisper("All pets have been kicked from the room.");
    }
}
