using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class MipCommand : ITargetChatCommand
{
    private readonly IDatabase _database;
    private readonly IModerationManager _moderationManager;
    public string Key => "mip";
    public string PermissionRequired => "command_mip";

    public string Parameters => "%username%";

    public string Description => "Machine ban, IP ban and account ban another user.";

    public bool MustBeInSameRoom => false;

    public MipCommand(IDatabase database, IModerationManager moderationManager)
    {
        _database = database;
        _moderationManager = moderationManager;
    }

    public async Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        if (target.Permissions.HasRight("mod_tool") && !session.GetHabbo().Permissions.HasRight("mod_ban_any"))
        {
            session.SendWhisper("Oops, you cannot ban that user.");
            return;
        }
        var ipAddress = string.Empty;
        var expire = UnixTimestamp.GetNow() + 78892200;
        var username = target.Username;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.RunQuery($"UPDATE `user_info` SET `bans` = `bans` + '1' WHERE `user_id` = '{target.Id}' LIMIT 1");
            dbClient.SetQuery($"SELECT `ip_last` FROM `users` WHERE `id` = '{target.Id}' LIMIT 1");
            ipAddress = dbClient.GetString();
        }
        string reason;
        if (parameters.Any())
            reason = CommandManager.MergeParams(parameters);
        else
            reason = "No reason specified.";
        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        if (!string.IsNullOrEmpty(ipAddress))
            await _moderationManager.BanUser(session.GetHabbo().Username, ModerationBanType.Ip, ipAddress, reason, expire, deadline.Token);
        await _moderationManager.BanUser(session.GetHabbo().Username, ModerationBanType.Username, target.Username, reason, expire, deadline.Token);
        if (!string.IsNullOrEmpty(target.MachineId))
            await _moderationManager.BanUser(session.GetHabbo().Username, ModerationBanType.Machine, target.MachineId, reason, expire, deadline.Token);
        target.Client?.Disconnect();
        session.SendWhisper($"Success, you have machine, IP and account banned the user '{username}' for '{reason}'!");
    }
}