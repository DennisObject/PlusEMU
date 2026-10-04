using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.Utilities;

namespace Plus.Communication.Packets.Incoming.Moderation;

internal class ModerationBanEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;
    private readonly IModerationManager _moderationManager;
    private readonly IDatabase _database;

    public ModerationBanEvent(IGameClientManager clientManager, IModerationManager moderationManager, IDatabase database)
    {
        _clientManager = clientManager;
        _moderationManager = moderationManager;
        _database = database;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().Permissions.HasRight("mod_soft_ban"))
            return;
        var userId = packet.ReadInt();
        var message = packet.ReadString();
        var length = packet.ReadInt() * 3600 + UnixTimestamp.GetNow();
        packet.ReadString(); //unk1
        packet.ReadString(); //unk2
        var ipBan = packet.ReadBool();
        var machineBan = packet.ReadBool();
        var targetClient = _clientManager.GetClientByUserId(userId);
        var habbo = targetClient?.GetHabbo();
        if (habbo == null)
        {
            session.SendWhisper("An error occoured whilst finding that user in the database.");
            return;
        }
        if (habbo.Permissions.HasRight("mod_tool") && !session.GetHabbo().Permissions.HasRight("mod_ban_any"))
        {
            session.SendWhisper("Oops, you cannot ban that user.");
            return;
        }
        message = message ?? "No reason specified.";
        var moderator = session.GetHabbo().Username;
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
        var machineId = targetClient!.MachineId;
#pragma warning restore CS0618
        using (var connection = _database.Connection())
            connection.Execute("UPDATE `user_info` SET `bans` = `bans` + 1 WHERE `user_id` = @userId LIMIT 1", new { userId = habbo.Id });

        // IP and machine bans also ban the account, as :ipban and :mip do.
        await _moderationManager.BanUser(moderator, ModerationBanType.Username, habbo.Username, message, length);
        if ((ipBan || machineBan) && AccountAddress(habbo.Id) is { Length: > 0 } address)
            await _moderationManager.BanUser(moderator, ModerationBanType.Ip, address, message, length);
        if (machineBan && !string.IsNullOrEmpty(machineId))
            await _moderationManager.BanUser(moderator, ModerationBanType.Machine, machineId, message, length);
    }

    /// <summary>
    /// The address the server knows for the account: users.ip_last, recorded by the auth API through its trusted proxies.
    /// The game socket's own address is the proxy's, so it is never used.
    /// </summary>
    private string AccountAddress(int userId)
    {
        using var connection = _database.Connection();
        return connection.ExecuteScalar<string?>("SELECT `ip_last` FROM `users` WHERE `id` = @userId", new { userId }) ?? string.Empty;
    }
}
