using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class IpBanCommand : ITargetChatCommand
{
    private readonly IModerationManager _moderationManager;
    public string Key => "ipban";

    public string Parameters => "%username%";

    public string Description => "IP and account ban another user.";

    public bool MustBeInSameRoom => true;

    private readonly TimeProvider _clock;

    public IpBanCommand(IModerationManager moderationManager, TimeProvider clock)
    {
        _moderationManager = moderationManager;
        _clock = clock;
    }

    public async Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        if (!session.GetHabbo().Access.Outranks(target.Access))
        {
            session.SendWhisper("Oops, you cannot ban that user.");
            return;
        }
        var expiresAt = _clock.GetUtcNow().AddSeconds(78892200);
        var username = target.Username;
        string reason;
        if (parameters.Any())
            reason = CommandManager.MergeParams(parameters);
        else
            reason = "No reason specified.";
        await _moderationManager.BanAccount(session.GetHabbo().Username, target.Id, target.Username, reason, expiresAt, deadline.Token, includeAddress: true);
        target.Client?.Disconnect();
        session.SendWhisper($"Success, you have IP and account banned the user '{username}' for '{reason}'!");
    }
}
