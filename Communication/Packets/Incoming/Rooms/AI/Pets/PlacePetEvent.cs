using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets;

internal class PlacePetEvent : RoomPacketEvent
{
    private readonly ILogger<PlacePetEvent> _logger;
    private readonly IRoomManager _roomManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IDatabase _database;

    public PlacePetEvent(IRoomManager roomManager, ISettingsManager settingsManager, ILogger<PlacePetEvent> logger, IDatabase database)
    {
        _roomManager = roomManager;
        _settingsManager = settingsManager;
        _logger = logger;
        _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.AllowPets && !room.CheckRights(session, true))
        {
            session.Send(new RoomErrorNotifComposer(PetPlacementError.RoomDisallowsPets));
            return Task.CompletedTask;
        }
        if (room.GetRoomUserManager().PetCount >= Convert.ToInt32(_settingsManager.TryGetValue("room.pets.placement_limit")))
        {
            session.Send(new RoomErrorNotifComposer(PetPlacementError.RoomLimitReached)); //5 = I have too many.
            return Task.CompletedTask;
        }
        if (!session.GetHabbo().Inventory.Pets.Pets.TryGetValue(packet.ReadInt(), out var pet) || pet.PetId <= 0)
            return Task.CompletedTask;
        if (pet.PlacedInRoom)
        {
            session.SendNotification("This pet is already in the room?");
            return Task.CompletedTask;
        }
        var x = packet.ReadInt();
        var y = packet.ReadInt();
        if (!room.GetGameMap().ValidTile(x, y) || !room.GetGameMap().SquareIsOpen(x, y, false) || !room.GetGameMap().CanWalk(x, y, false))
        {
            session.Send(new RoomErrorNotifComposer(PetPlacementError.InvalidTile));
            return Task.CompletedTask;
        }
        var z = room.GetGameMap().SqAbsoluteHeight(x, y);
        if (!pet.TrySaveRoom(_database, room.RoomId, x, y, z))
            return Task.CompletedTask;
        if (room.GetRoomUserManager().TryGetPet(pet.PetId, out var oldPet)) room.GetRoomUserManager().RemoveBot(oldPet.VirtualId, false);
        pet.X = x;
        pet.Y = y;
        pet.Z = z;
        pet.PlacedInRoom = true;
        pet.RoomId = room.RoomId;
        var rndSpeechList = new List<RandomSpeech>();
        var roomBot = new RoomBot(pet.PetId, pet.RoomId, "pet", "freeroam", pet.Name, "", pet.Look, x, y, z, 0, 0, 0, 0, 0, ref rndSpeechList, "", 0, pet.OwnerId, false, 0, false, 0);
        room.GetRoomUserManager().DeployBot(roomBot, pet);
        pet.DbState = PetDatabaseUpdateState.Updated;
        session.GetHabbo().Inventory.Pets.RemovePet(pet.PetId);
        session.Send(new PetInventoryComposer(PetAppearanceSnapshots.Inventory(session.GetHabbo().Inventory.Pets.Pets.Values.ToList())));
        return Task.CompletedTask;
    }
}
