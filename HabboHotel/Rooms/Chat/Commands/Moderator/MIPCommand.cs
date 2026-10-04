using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class MipCommand : ITargetChatCommand
{
    private readonly IModerationManager _moderationManager;
    public string Key => "mip";
    public string PermissionRequired => "command_mip";

    public string Parameters => "%username%";

    public string Description => "Machine ban, IP ban and account ban another user.";

    public bool MustBeInSameRoom => false;

    public MipCommand(IModerationManager moderationManager)
    {
        _moderationManager = moderationManager;
    }

    public async Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        if (target.Permissions.HasRight("mod_tool") && !session.GetHabbo().Permissions.HasRight("mod_ban_any"))
        {
            session.SendWhisper("Oops, you cannot ban that user.");
            return;
        }
        var expire = BanClock.Now() + 78892200;
        var username = target.Username;
        string reason;
        if (parameters.Any())
            reason = CommandManager.MergeParams(parameters);
        else
            reason = "No reason specified.";
#pragma warning disable CS0618 // The handshake's machine id lives on the session; the stored one is the fallback.
        var machineId = string.IsNullOrEmpty(target.Client?.MachineId) ? target.MachineId : target.Client.MachineId;
#pragma warning restore CS0618
        await _moderationManager.BanAccount(session.GetHabbo().Username, target.Id, target.Username, reason, expire, deadline.Token,
            includeAddress: true, machineId: machineId);
        target.Client?.Disconnect();
        session.SendWhisper($"Success, you have machine, IP and account banned the user '{username}' for '{reason}'!");
    }
}
