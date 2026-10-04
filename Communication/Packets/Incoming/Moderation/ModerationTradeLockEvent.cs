using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTradeLock)]
internal class ModerationTradeLockEvent : IPacketEvent
{
    public readonly IDatabase _database;

    public ModerationTradeLockEvent(IDatabase database)
    {
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var message = packet.ReadString();
        var days = packet.ReadInt() / 1440.0;
        packet.ReadString(); //unk1
        packet.ReadString(); //unk2
        var length = PlusEnvironment.GetUnixTimestamp() + days * 86400;
        var habbo = PlusEnvironment.GetHabboById(userId);
        if (habbo == null)
        {
            session.SendWhisper("An error occoured whilst finding that user in the database.");
            return Task.CompletedTask;
        }
        if (!session.GetHabbo().Access.Outranks(habbo.Access))
        {
            session.SendWhisper("Oops, you cannot trade lock another user ranked 5 or higher.");
            return Task.CompletedTask;
        }
        if (days < 1)
            days = 1;
        if (days > 365)
            days = 365;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.RunQuery($"UPDATE `user_info` SET `trading_locked` = '{length}', `trading_locks_count` = `trading_locks_count` + '1' WHERE `user_id` = '{habbo.Id}' LIMIT 1");
        }
        if (habbo.Client != null)
        {
            habbo.TradingLockExpiry = length;
            habbo.Client.SendNotification($"You have been trade banned for {days} day(s)!\r\rReason:\r\r{message}");
        }
        return Task.CompletedTask;
    }
}