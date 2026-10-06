using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Catalog;

public sealed record PetPaletteEntry(int Breed, int Palette);
public sealed record PetPaletteSnapshot(string Type, int PetId, ImmutableArray<PetPaletteEntry> Races)
{
    public static PetPaletteSnapshot Capture(string type, int petId, IEnumerable<PetRace> races) =>
        new(type, petId, races.Select(race => new PetPaletteEntry(race.PrimaryColour, race.SecondaryColour)).ToImmutableArray());
}
public sealed record PromotableRoomSnapshot(uint Id, string Name);

public interface ICatalogBrowsingService
{
    void ShowPetPalettes(GameClient session, string type);
    void ShowPromotableRooms(GameClient session);
}

public sealed class CatalogBrowsingService(IItemDataManager items, IPetRaceManager races, IRoomDataLoader rooms,
    TimeProvider clock) : ICatalogBrowsingService
{
    public void ShowPetPalettes(GameClient session, string type)
    {
        var item = items.GetItemByName(type);
        if (item == null)
            return;
        session.Send(new SellablePetBreedsComposer(PetPaletteSnapshot.Capture(type, item.BehaviourData,
            races.GetRacesForRaceId(item.BehaviourData))));
    }

    public void ShowPromotableRooms(GameClient session) =>
        session.Send(new PromotableRoomsComposer(CapturePromotableRooms(session.GetHabbo().Id)));

    internal ImmutableArray<PromotableRoomSnapshot> CapturePromotableRooms(int ownerId)
    {
        var data = rooms.GetRoomsDataByOwnerSortByName(ownerId);
        var now = clock.GetUtcNow();
        return data.Where(room => room.Promotion == null || room.Promotion.HasExpiredAt(now))
            .Select(room => new PromotableRoomSnapshot(room.Id, room.Name)).ToImmutableArray();
    }
}
