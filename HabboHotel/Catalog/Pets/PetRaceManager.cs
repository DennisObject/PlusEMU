using Dapper;
using Plus.Core;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Pets;

public class PetRaceManager : IPetRaceManager, IStartable
{
    private readonly IDatabase _database;
    private readonly List<PetRace> _races = new();

    public PetRaceManager(IDatabase database)
    {
        _database = database;
    }
    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var races = await connection.QueryAsync<PetRace>("SELECT raceid AS RaceId, color1 AS PrimaryColour, color2 AS SecondaryColour, has1color AS HasPrimaryColour, has2color AS HasSecondaryColour FROM catalog_pet_races");
        _races.Clear();
        _races.AddRange(races);
    }

    public List<PetRace> GetRacesForRaceId(int raceId)
    {
        return _races.Where(race => race.RaceId == raceId).ToList();
    }
}
