using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users;
using System.Globalization;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class BanCommand : ITargetChatCommand
{
    private readonly IModerationManager _moderationManager;
    public string Key => "ban";

    public string Parameters => "%username% %length% %reason% ";

    public string Description => "Remove a toxic player from the hotel for a fixed amount of time.";

    public bool MustBeInSameRoom => false;

    private readonly TimeProvider _clock;

    public BanCommand(IModerationManager moderationManager, TimeProvider clock)
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
        var now = _clock.GetUtcNow();
        var hours = parameters[0];
        DateTimeOffset expiresAt;
        if (string.IsNullOrEmpty(hours) || hours == "perm")
        {
            if (!ModerationBanDuration.TryGetExpiry(now, TimeSpan.FromSeconds(78892200), out expiresAt))
                return;
        }
        else
        {
            if (!double.TryParse(hours, NumberStyles.Float, CultureInfo.CurrentCulture, out var duration) ||
                !ModerationBanDuration.TryGetExpiry(now, duration, out expiresAt))
                return;
        }
        string reason;
        if (parameters.Length >= 2)
            reason = CommandManager.MergeParams(parameters, 1);
        else
            reason = "No reason specified.";
        var username = target.Username;
        await _moderationManager.BanAccount(session.GetHabbo().Username, target.Id, target.Username, reason, expiresAt, deadline.Token);
        target.Client?.Disconnect();
        session.SendWhisper($"Success, you have account banned the user '{username}' for {hours} hour(s) with the reason '{reason}'!");
    }
}
