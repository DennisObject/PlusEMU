using Plus.Database;
using Plus.HabboHotel.GameClients;
using Dapper;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class DisableForcedFxCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "forced_effects";

    public string Parameters => "";

    public string Description => "Gives you the ability to ignore or allow forced effects.";

    public DisableForcedFxCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var habbo = session.GetHabbo();
        var value = !habbo.DisableForcedEffects;
        using var connection = _database.Connection();
        if (connection.Execute("UPDATE users_settings SET disable_forced_effects = @value WHERE user_id = @userId LIMIT 1",
                new { value, userId = habbo.Id }) != 1)
            throw new InvalidOperationException("User settings were not persisted.");
        habbo.DisableForcedEffects = value;
        session.SendWhisper($"Forced FX mode is now {(value ? "disabled!" : "enabled!")}");
    }
}