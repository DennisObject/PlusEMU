using Dapper;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.AI;

public interface IHorseCustomizationService
{
    void RemoveSaddle(GameClient session, int petId);
    void ToggleRiding(Room room, GameClient session, int petId);
    void ApplyEffect(Room room, GameClient session, uint itemId, int petId);
}

public interface IHorseCustomizationStore
{
    void UpdatePet(int petId, string column, object value);
    void ConsumeItem(int petId, string column, object value, uint itemId, uint roomId, int ownerId);
}

public sealed class HorseCustomizationStore(IDatabase database) : IHorseCustomizationStore
{
    private static readonly HashSet<string> Columns = ["have_saddle", "anyone_ride", "pethair", "hairdye", "race"];

    public void UpdatePet(int petId, string column, object value)
    {
        if (!Columns.Contains(column)) throw new ArgumentOutOfRangeException(nameof(column));
        using var connection = database.Connection();
        if (connection.Execute($"UPDATE bots_petdata SET `{column}`=@value WHERE id=@petId LIMIT 1", new { value, petId }) != 1)
            throw new InvalidOperationException("The horse customization was not persisted.");
    }

    public void ConsumeItem(int petId, string column, object value, uint itemId, uint roomId, int ownerId)
    {
        if (!Columns.Contains(column)) throw new ArgumentOutOfRangeException(nameof(column));
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.Execute($"UPDATE bots_petdata SET `{column}`=@value WHERE id=@petId LIMIT 1", new { value, petId }, transaction) != 1 ||
            connection.Execute("DELETE FROM items WHERE id=@itemId AND room_id=@roomId AND user_id=@ownerId LIMIT 1",
                new { itemId, roomId, ownerId }, transaction) != 1)
            throw new InvalidOperationException("The horse customization was not persisted.");
        transaction.Commit();
    }
}

public sealed class HorseCustomizationService(
    IRoomManager roomManager,
    IItemDataManager itemDataManager,
    IItemFactory itemFactory,
    IHorseCustomizationStore store) : IHorseCustomizationService
{
    public void RemoveSaddle(GameClient session, int petId)
    {
        var habbo = session.GetHabbo();
        if (!habbo.InRoom || habbo.CurrentRoom == null || !roomManager.TryGetRoom(habbo.CurrentRoom.Id, out var room)) return;
        if (!room.GetRoomUserManager().TryGetPet(petId, out var petUser) || petUser.PetData?.OwnerId != habbo.Id) return;
        var saddleId = ItemUtility.GetSaddleId(petUser.PetData.Saddle);
        store.UpdatePet(petUser.PetData.PetId, "have_saddle", 0);
        petUser.PetData.Saddle = 0;
        if (itemDataManager.Items.TryGetValue(saddleId, out var itemData))
        {
            var item = itemFactory.CreateSingleItemNullable(itemData, habbo, "", "")?.ToInventoryItem();
            if (item != null)
            {
                habbo.Inventory.Furniture.AddItem(item);
                session.Send(new FurniListNotificationComposer(item.Id, 1));
                session.Send(new PurchaseOKComposer());
                session.Send(new FurniListAddComposer(item));
                session.Send(new FurniListUpdateComposer());
            }
        }
        room.SendPacket(new UsersComposer(petUser));
        room.SendPacket(new PetHorseFigureInformationComposer(petUser));
    }

    public void ToggleRiding(Room room, GameClient session, int petId)
    {
        if (!room.GetRoomUserManager().TryGetPet(petId, out var pet) || pet.PetData?.OwnerId != session.GetHabbo().Id) return;
        var anyoneCanRide = pet.PetData.AnyoneCanRide == 1 ? 0 : 1;
        store.UpdatePet(pet.PetData.PetId, "anyone_ride", anyoneCanRide);
        pet.PetData.AnyoneCanRide = anyoneCanRide;
        room.SendPacket(new PetInformationComposer(pet.PetData));
    }

    public void ApplyEffect(Room room, GameClient session, uint itemId, int petId)
    {
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary || item.UserId != session.GetHabbo().Id) return;
        if (!room.GetRoomUserManager().TryGetPet(petId, out var petUser) || petUser.PetData?.OwnerId != session.GetHabbo().Id) return;
        var change = GetChange(item);
        if (change != null)
        {
            store.ConsumeItem(petUser.PetData.PetId, change.Value.Column, change.Value.Value, item.Id, room.Id, session.GetHabbo().Id);
            change.Value.Publish(petUser.PetData);
            room.GetRoomItemHandler().RemoveFurniture(session, item.Id);
        }
        room.SendPacket(new UsersComposer(petUser));
        room.SendPacket(new PetHorseFigureInformationComposer(petUser));
    }

    private static HorseChange? GetChange(Item item) => item.Definition.InteractionType switch
    {
        InteractionType.HorseSaddle1 => new("have_saddle", 9, pet => pet.Saddle = 9),
        InteractionType.HorseSaddle2 => new("have_saddle", 10, pet => pet.Saddle = 10),
        InteractionType.HorseHairstyle => Parse(item, 100, "pethair", (pet, value) => pet.PetHair = value),
        InteractionType.HorseHairDye => Parse(item, 48, "hairdye", (pet, value) => pet.HairDye = value),
        InteractionType.HorseBodyDye => BodyDye(item),
        _ => null
    };

    private static HorseChange Parse(Item item, int offset, string column, Action<Pet, int> publish)
    {
        var value = offset + int.Parse(item.Definition.ItemName.Split('_')[2]);
        return new(column, value, pet => publish(pet, value));
    }

    private static HorseChange BodyDye(Item item)
    {
        var race = int.Parse(item.Definition.ItemName.Split('_')[2]);
        var value = race switch { 13 => 61, 14 => 65, 15 => 69, 16 => 73, _ => 2 + race * 4 - 4 };
        return new("race", value.ToString(), pet => pet.Race = value.ToString());
    }

    private readonly record struct HorseChange(string Column, object Value, Action<Pet> Publish);
}
