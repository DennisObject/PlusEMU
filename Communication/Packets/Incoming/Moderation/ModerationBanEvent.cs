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
        // Fail closed first, from memory; then one deadline covers every step of this compound ban.
        targetClient.Disconnect();
        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        try
        {
            using var connection = _database.Connection();
            await connection.ExecuteAsync(new CommandDefinition("UPDATE `user_info` SET `bans` = `bans` + 1 WHERE `user_id` = @userId LIMIT 1",
                new { userId = habbo.Id }, cancellationToken: deadline.Token));
        }
        catch (OperationCanceledException)
        {
            // Only the ban counter; the bans themselves continue below and in the background.
        }

        // IP and machine bans also ban the account, as :ipban and :mip do.
        await _moderationManager.BanUser(moderator, ModerationBanType.Username, habbo.Username, message, length, deadline.Token);
        if ((ipBan || machineBan) && await AccountAddress(habbo.Id, deadline.Token) is { Length: > 0 } address)
            await _moderationManager.BanUser(moderator, ModerationBanType.Ip, address, message, length, deadline.Token);
        if (machineBan && !string.IsNullOrEmpty(machineId))
            await _moderationManager.BanUser(moderator, ModerationBanType.Machine, machineId, message, length, deadline.Token);
    }

    /// <summary>
    /// The address the server knows for the account: users.ip_last, recorded by the auth API through its trusted proxies.
    /// The game socket's own address is the proxy's, so it is never used. Empty when it cannot be read in time.
    /// </summary>
    private async Task<string> AccountAddress(int userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _database.Connection();
            return await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT `ip_last` FROM `users` WHERE `id` = @userId",
                new { userId }, cancellationToken: cancellationToken)) ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
