using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Moderation;
using System.Text;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class UserInfoCommand : IChatCommand
{
    private readonly IModerationUserStore _users;
    private readonly IAccessControl _access;
    private readonly IGameClientManager _gameClientManager;
    public string Key => "userinfo";

    public string Parameters => "%username%";

    public string Description => "View another users profile information.";

    public UserInfoCommand(IModerationUserStore users, IGameClientManager gameClientManager, IAccessControl access)
    {
        _users = users;
        _access = access;
        _gameClientManager = gameClientManager;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (parameters.Length < 2)
        {
            session.SendWhisper("Please enter the username of the user you wish to view.");
            return;
        }
        var username = parameters[1];
        var userData = _users.Find(username);
        if (userData == null)
        {
            session.SendNotification($"Oops, there is no user in the database with that username ({username})!");
            return;
        }
        var targetClient = _gameClientManager.GetClientByUsername(username);
        var habboInfo = new StringBuilder();
        habboInfo.Append($"{userData.Username}'s account:\r\r");
        habboInfo.Append("Generic Info:\r");
        habboInfo.Append($"ID: {userData.Id}\r");
        habboInfo.Append($"Rank: {userData.Rank}\r");
        habboInfo.Append($"Roles: {string.Join(", ", _access.Resolve(userData.Id).Roles.Select(role => role.Name))}\r");
        habboInfo.Append($"Email: {userData.Mail}\r");
        habboInfo.Append($"Online Status: {(targetClient != null ? "True" : "False")}\r\r");
        habboInfo.Append("Currency Info:\r");
        habboInfo.Append($"Credits: {userData.Credits}\r");
        habboInfo.Append($"Duckets: {userData.Duckets}\r");
        habboInfo.Append($"Diamonds: {userData.Diamonds}\r");
        habboInfo.Append($"GOTW Points: {userData.GotwPoints}\r\r");
        habboInfo.Append("Moderation Info:\r");
        habboInfo.Append($"Bans: {userData.Bans}\r");
        habboInfo.Append($"CFHs Sent: {userData.HelpRequests}\r");
        habboInfo.Append($"Abusive CFHs: {userData.AbusiveHelpRequests}\r");
        habboInfo.Append($"Trading Locked: {(userData.TradingLockExpiresAt == null ? "No outstanding lock" : $"Expiry: {userData.TradingLockExpiresAt:dd/MM/yyyy}")}\r");
        habboInfo.Append($"Amount of trading locks: {userData.TradingLockCount}\r\r");
        if (targetClient != null)
        {
            habboInfo.Append("Current Session:\r");
            if (targetClient.GetHabbo().CurrentRoom is not { } currentRoom)
                habboInfo.Append("Currently not in a room.\r");
            else
            {
                habboInfo.Append($"Room: {currentRoom.Name} ({currentRoom.RoomId})\r");
                habboInfo.Append($"Room Owner: {currentRoom.OwnerName}\r");
                habboInfo.Append($"Current Visitors: {currentRoom.UserCount}/{currentRoom.UsersMax}");
            }
        }
        session.SendNotification(habboInfo.ToString());
    }
}