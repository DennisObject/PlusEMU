using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.Utilities;
using Dapper;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class SendRoomInviteEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;
    private readonly IDatabase _database;

    public SendRoomInviteEvent(IGameClientManager clientManager, IDatabase database)
    {
        _clientManager = clientManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (session.GetHabbo().TimeMuted > 0)
        {
            session.SendNotification("Oops, you're currently muted - you cannot send room invitations.");
            return Task.CompletedTask;
        }
        var amount = packet.ReadInt();
        if (amount > 500)
            return Task.CompletedTask; // don't send at all
        var targets = new List<int>();
        for (var i = 0; i < amount; i++)
        {
            var uid = packet.ReadInt();
            if (i < 100) // limit to 100 people, keep looping until we fulfil the request though
                targets.Add(uid);
        }
        var message = StringCharFilter.Escape(packet.ReadString());
        if (message.Length > 121)
            message = message.Substring(0, 121);
        var delivered = false;
        foreach (var userId in targets)
        {
            if (!session.GetHabbo().Messenger.FriendshipExists(userId))
                continue;
            var client = _clientManager.GetClientByUserId(userId);
            if (client == null || client.GetHabbo() == null || client.GetHabbo().AllowMessengerInvites || client.GetHabbo().AllowConsoleMessages == false)
                continue;
            client.Send(new RoomInviteComposer(session.GetHabbo().Id, message));
            delivered = true;
        }
        if (delivered)
            RewardTrackManager.Current?.Progress(session, RewardTrackActions.SendMessengerInvite);
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO `chatlogs_console_invitations` (`user_id`,`message`,`timestamp`) VALUES (@userId, @message, UNIX_TIMESTAMP())",
            new { userId = session.GetHabbo().Id, message = message });
        return Task.CompletedTask;
    }
}