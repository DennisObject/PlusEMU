using Dapper;
using Plus.Core;
using Plus.Database;

namespace Plus.HabboHotel.Rooms.Chat.Pets.Commands;

public class PetCommandManager : IPetCommandManager, IStartable
{
    private readonly IDatabase _database;
    private readonly Dictionary<string, PetCommand> _petCommands;

    public PetCommandManager(IDatabase database)
    {
        _database = database;
        _petCommands = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var commands = await connection.QueryAsync<(int Id, string Input)>("SELECT id, COALESCE(input, '') FROM bots_pet_commands");
        _petCommands.Clear();

        foreach (var row in commands) {
            foreach (var command in row.Input.Split(',')) {
                _petCommands.Add(command, new(row.Id, command));
            }
        }
    }

    public int TryInvoke(string input)
    {
        if (_petCommands.TryGetValue(input.ToLower(), out var command)) {
            return command.Id;
        }

        return 0;
    }
}
