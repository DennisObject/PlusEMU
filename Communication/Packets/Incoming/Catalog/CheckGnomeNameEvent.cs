using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI.Speech;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class CheckGnomeNameEvent : RoomPacketEvent
{
    private readonly IDatabase _database;
    private readonly IItemDataManager _itemDataManager;
    private readonly IItemFactory _itemFactory;

    public CheckGnomeNameEvent(IDatabase database, IItemDataManager itemDataManager, IItemFactory itemFactory)
    {
        _database = database;
        _itemDataManager = itemDataManager;
        _itemFactory = itemFactory;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.Definition == null || item.OwnerId != session.GetHabbo().Id || item.Definition.InteractionType != InteractionType.GnomeBox)
            return Task.CompletedTask;
        var petName = packet.ReadString();
        if (string.IsNullOrEmpty(petName))
        {
            session.Send(new CheckGnomeNameComposer(petName, 1));
            return Task.CompletedTask;
        }
        if (!PetUtility.CheckPetName(petName))
        {
            session.Send(new CheckGnomeNameComposer(petName, 1));
            return Task.CompletedTask;
        }
        var x = item.GetX;
        var y = item.GetY;

        //Create the pet here.
        var pet = PetUtility.CreatePet(_database, session.GetHabbo().Id, petName, 26, "30", "ffffff", item, RandomClothing());
        if (pet == null)
        {
            session.SendNotification("Oops, an error occoured. Please report this!");
            return Task.CompletedTask;
        }
        room.GetRoomItemHandler().RemoveFurniture(session, item.Id);
        session.Send(new CheckGnomeNameComposer(petName, 0));
        var rndSpeechList = new List<RandomSpeech>();

        //Make a RoomUser of the pet.
        room.GetRoomUserManager()
            .DeployBot(new(pet.PetId, pet.RoomId, "pet", "freeroam", pet.Name, "", pet.Look, x, y, pet.Z, 0, 0, 0, 0, 0, ref rndSpeechList, "", 0, pet.OwnerId, false, 0, false, 0), pet);

        //Give the food.
        if (_itemDataManager.Items.TryGetValue(320, out var petFood))
        {
            var food = _itemFactory.CreateSingleItemNullable(petFood, session.GetHabbo(), "", "").ToInventoryItem();
            if (food != null)
            {
                session.GetHabbo().Inventory.Furniture.AddItem(food);
                session.Send(new FurniListNotificationComposer(food.Id, 1));
            }
        }
        return Task.CompletedTask;
    }

    private static string RandomClothing()
    {
        var randomNumber = Random.Shared.Next(1, 7);
        switch (randomNumber)
        {
            default:
                return "5 0 -1 0 4 402 5 3 301 4 1 101 2 2 201 3";
            case 2:
                return "5 0 -1 0 1 102 13 3 301 4 4 401 5 2 201 3";
            case 3:
                return "5 1 102 8 2 201 16 4 401 9 3 303 4 0 -1 6";
            case 4:
                return "5 0 -1 0 3 303 4 4 401 5 1 101 2 2 201 3";
            case 5:
                return "5 3 302 4 2 201 11 1 102 12 0 -1 28 4 401 24";
            case 6:
                return "5 4 402 5 3 302 21 0 -1 7 1 101 12 2 201 17";
        }
    }
}