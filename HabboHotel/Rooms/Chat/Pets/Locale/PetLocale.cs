using Dapper;
using Plus.Database;
using Plus.Core;

namespace Plus.HabboHotel.Rooms.Chat.Pets.Locale;

public class PetLocale : IPetLocale, IStartable
{
    private Dictionary<string, string[]> _values;

    private readonly IDatabase _database;

    public PetLocale(IDatabase database)
    {
        _database = database;
        _values = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var responses = await connection.QueryAsync<(string Key, string Responses)>("SELECT pet_id, responses FROM bots_pet_responses");
        _values = responses.ToDictionary(row => row.Key, row => row.Responses.Split(';'));
    }

    public string[] GetValue(string key)
    {
        if (_values.TryGetValue(key, out var value))
            return value;
        return new[] { $"Unknown pet speach:{key}" };
    }
}