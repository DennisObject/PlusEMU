using Dapper;
using Plus.Core;
using Plus.Database;

namespace Plus.HabboHotel.Rooms.Chat.Pets.Commands;

public class PetCommandManager : IPetCommandManager, IStartable
{
    private readonly IDatabase _database;
    private readonly Dictionary<string, string> _commandDatabase;
    private readonly Dictionary<int, string> _commandRegister;
    private readonly Dictionary<string, PetCommand> _petCommands;

    public PetCommandManager(IDatabase database)
    {
        _database = database;
        _petCommands = new();
        _commandRegister = new();
        _commandDatabase = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var commands = await connection.QueryAsync<(int Id, string Title, string Input)>("SELECT id, input_title, COALESCE(input, '') FROM bots_pet_commands");
        _petCommands.Clear();
        _commandRegister.Clear();
        _commandDatabase.Clear();

        foreach (var row in commands) {
            _commandRegister.Add(row.Id, row.Title);
            _commandDatabase.Add($"{row.Title}.input", row.Input);

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
