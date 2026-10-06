using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;
using Plus.Utilities;

namespace Plus.Communication.Packets.Incoming.FriendList;

// The modern client sends Habicons through this header. Legacy text still uses SendMsgEvent.
public sealed class SendMessengerMessageEvent(IHabbiconService habbicons, IDatabase database,
    IGameClientManager clients, ILogger<SendMessengerMessageEvent> logger) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int conversationId = packet.ReadInt(), recipientId = packet.ReadInt(), confirmationId = packet.ReadInt(), type = packet.ReadInt();
        string message = packet.ReadString(), metadata = packet.ReadString();
        var sender = session.GetHabbo();
        try
        {
            if (conversationId != 0 || recipientId <= 0 || recipientId == sender.Id || type != 4 ||
                !int.TryParse(message, out int id) || id <= 0 || id > 1000000)
                throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
            if (sender.Messenger.GetFriend(recipientId) == null || metadata.Length != 0) throw new HabbiconRejected(HabbiconActionError.MessageForbidden);
            var item = habbicons.Load(sender.Id).RequireItem(id);
            if (!item.Owned) throw new HabbiconRejected(HabbiconActionError.MessageForbidden);
            if (sender.TimeMuted > 0 || UnixTimestamp.GetNow() < sender.FloodTime || !sender.Messenger.TrySendHabbicon())
                throw new HabbiconRejected(HabbiconActionError.MessageRateLimited);
            var target = clients.GetClientByUserId(recipientId);
            if (target != null && (target.GetHabbo().TimeMuted > 0 || !target.GetHabbo().AllowConsoleMessages ||
                target.GetHabbo().IgnoresComponent.IsIgnored(sender.Id) || target.GetHabbo().Messenger.GetFriend(sender.Id) == null))
                throw new HabbiconRejected(HabbiconActionError.MessageForbidden);
            int createdAt = checked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // Plus retains its existing audit and offline text storage; no new messenger history schema.
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute("""
                INSERT INTO chatlogs_console (from_id, to_id, message, timestamp) VALUES (@senderId, @recipientId, @fallback, @createdAt)
                """, new { senderId = sender.Id, recipientId, fallback = ":" + item.Name + ":", createdAt }, transaction);
            int messageId = connection.QuerySingle<int>("SELECT LAST_INSERT_ID()", transaction: transaction);
            if (target == null)
                connection.Execute("""
                    INSERT INTO messenger_offline_messages (from_id, to_id, message, timestamp) VALUES (@senderId, @recipientId, @fallback, @createdAt)
                    """, new { senderId = sender.Id, recipientId, fallback = ":" + item.Name + ":", createdAt }, transaction);
            transaction.Commit();
            session.Send(new MessengerMessageAckComposer(confirmationId, messageId, createdAt));
            target?.Send(new MessengerMessageComposer(messageId, sender.Id, id, createdAt));
            try
            {
                if (habbicons.Use(sender.Id, id)) session.Send(new UserHabbiconsComposer(habbicons.Load(sender.Id)));
            }
            catch (MySqlException exception)
            {
                logger.LogWarning(exception, "Unable to update recent Habicons for {UserId}", sender.Id);
            }
        }
        catch (HabbiconRejected rejected)
        {
            session.Send(new MessengerMessageFailedComposer(confirmationId, rejected.Code));
        }
        catch (MySqlException exception)
        {
            logger.LogError(exception, "Unable to send Habicon for {UserId}", sender.Id);
            session.Send(new MessengerMessageFailedComposer(confirmationId, 7));
        }
        return Task.CompletedTask;
    }
}
