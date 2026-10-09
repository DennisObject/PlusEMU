using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.HabboHotel.Friends;

public interface IHabbiconMessengerService
{
    void Send(GameClient session, int conversationId, int recipientId, int confirmationId, int type, string message, string metadata);
}

public sealed class HabbiconMessengerService(IHabbiconService habbicons, IGameClientManager clients, IHabbiconMessengerStore store,
    ILogger<HabbiconMessengerService> logger, TimeProvider clock) : IHabbiconMessengerService
{
    public void Send(GameClient session, int conversationId, int recipientId, int confirmationId, int type, string message, string metadata)
    {
        var sender = session.GetHabbo();
        var capturedAt = clock.GetUtcNow();

        try {
            if (conversationId != 0 || recipientId <= 0 || recipientId == sender.Id || type != 4 ||
                !int.TryParse(message, out int id) || id <= 0 || id > 1000000) {
                throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
            }

            if (sender.Messenger is not { } messenger || messenger.GetFriend(recipientId) == null || metadata.Length != 0) {
                throw new HabbiconRejected(HabbiconActionError.MessageForbidden);
            }

            var item = habbicons.Load(sender.Id).RequireItem(id);

            if (!item.Owned) {
                throw new HabbiconRejected(HabbiconActionError.MessageForbidden);
            }

            if (sender.TimeMuted > 0 || (sender.FloodUntil is { } floodUntil && capturedAt < floodUntil) || !messenger.TrySendHabbicon(capturedAt)) {
                throw new HabbiconRejected(HabbiconActionError.MessageRateLimited);
            }

            var target = clients.GetClientByUserId(recipientId);

            if (target != null && (target.GetHabbo().TimeMuted > 0 || !target.GetHabbo().AllowConsoleMessages ||
                target.GetHabbo().IgnoresComponent?.IsIgnored(sender.Id) == true || target.GetHabbo().Messenger?.GetFriend(sender.Id) == null)) {
                throw new HabbiconRejected(HabbiconActionError.MessageForbidden);
            }

            // Nothing is acknowledged or delivered unless the store has committed the audit row.
            var createdAtUtc = capturedAt.UtcDateTime;
            int messageId = store.Record(sender.Id, recipientId, ":" + item.Name + ":", createdAtUtc, target == null);
            int createdAt = MessengerTime.WireSeconds(capturedAt);
            session.Send(new MessengerMessageAckComposer(confirmationId, messageId, createdAt));
            target?.Send(new MessengerMessageComposer(messageId, sender.Id, id, createdAt));

            try {
                if (habbicons.Use(sender.Id, id)) {
                    session.Send(new UserHabbiconsComposer(habbicons.Load(sender.Id)));
                }
            }
            catch (MySqlException exception) {
                logger.LogWarning(exception, "Unable to update recent Habicons for {UserId}", sender.Id);
            }
        }
        catch (HabbiconRejected rejected) {
            session.Send(new MessengerMessageFailedComposer(confirmationId, rejected.Code));
        }
        catch (MySqlException exception) {
            logger.LogError(exception, "Unable to send Habicon for {UserId}", sender.Id);
            session.Send(new MessengerMessageFailedComposer(confirmationId, 7));
        }
    }
}
