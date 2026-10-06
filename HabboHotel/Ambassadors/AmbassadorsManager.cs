using Dapper;
using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Database;
using Plus.HabboHotel.GameClients;


namespace Plus.HabboHotel.Ambassadors;

public class AmbassadorsManager : IAmbassadorsManager
{
    private readonly IDatabase _database;
    private readonly TimeProvider _clock;
    private readonly IGameClientManager _clients;

    public AmbassadorsManager(IDatabase database, TimeProvider clock, IGameClientManager clients)
    {
        _database = database;
        _clock = clock;
        _clients = clients;
    }

    public async Task Warn(GameClient session, int targetId, string message)
    {
        var ambassador = session.GetHabbo();

        if (!ambassador.IsAmbassador)
        {
            return;
        }

        var targetClient = _clients.GetClientByUserId(targetId);
        var target = targetClient?.GetHabbo();

        if (targetClient == null || target == null)
        {
            return;
        }

        var targetName = target.Username;

        using var connection = _database.Connection();
        await connection.ExecuteAsync("INSERT INTO `ambassador_logs` (`user_id`,`target`,`sanctions_type`,`timestamp`) VALUES (@user_id,@target_name,@sanctions_type,@timestamp)",
            new
            {
                user_id = ambassador.Id,
                target_name = targetName,
                sanctions_type = message,
                timestamp = _clock.GetUtcNow().UtcDateTime
            });
        session.SendWhisper($"You have successfully warned {targetName}.");
        targetClient.Send(new RoomNotificationComposer("ambassador.alert.warning", "message", "${notification.ambassador.alert.warning.message}"));
    }
}
