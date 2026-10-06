using Plus.HabboHotel.Permissions;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class SetMaxCommand : IChatCommand
{
    private readonly IDatabase _database;
    private readonly Plus.Core.Settings.ISettingsManager _settings;
    public string Key => "setmax";

    public string Parameters => "%value%";

    public string Description => "Set the visitor limit to the room.";

    public SetMaxCommand(IDatabase database, Plus.Core.Settings.ISettingsManager settings)
    {
        _database = database;
        _settings = settings;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!room.CheckRights(session, true)) {
            return;
        }

        if (!parameters.Any()) {
            session.SendWhisper("Please enter a value for the room visitor limit.");

            return;
        }

        if (int.TryParse(parameters[0], out var maxAmount)) {
            var limit = Plus.HabboHotel.Subscriptions.ClubLimits.For(session.GetHabbo().Access, "visitors", _settings);

            if (maxAmount <= 0) {
                maxAmount = 10;
                session.SendWhisper("visitor amount too low, visitor amount has been set to 10.");
            }
            else if (maxAmount > limit && !session.GetHabbo().Access.Can(PermissionKeys.RoomUserLimitOverride)) {
                maxAmount = limit;
                session.SendWhisper("visitor amount exceeds your room visitor limit.");
            }
            else {
                session.SendWhisper($"visitor amount set to {maxAmount}.");
            }

            room.UsersMax = maxAmount;
            using var connection = _database.Connection();
            connection.Execute("UPDATE rooms SET users_max=@maxAmount WHERE id=@roomId LIMIT 1", new { maxAmount, roomId = room.Id });
        }
        else {
            session.SendWhisper("Invalid amount, please enter a valid number.");
        }
    }
}
