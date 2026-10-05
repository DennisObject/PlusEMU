using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationBanSoft)]
internal class ModerationBanEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;
    private readonly IModerationManager _moderationManager;
    private readonly TimeProvider _clock;

    public ModerationBanEvent(IGameClientManager clientManager, IModerationManager moderationManager, TimeProvider clock)
    {
        _clientManager = clientManager;
        _moderationManager = moderationManager;
        _clock = clock;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        var userId = packet.ReadInt();
        var message = packet.ReadString();
        var hours = packet.ReadInt();
        packet.ReadString(); //unk1
        packet.ReadString(); //unk2
        var ipBan = packet.ReadBool();
        var machineBan = packet.ReadBool();
        if (ipBan && !session.GetHabbo().Access.Can(PermissionKeys.ModerationIpBan) || machineBan && !session.GetHabbo().Access.Can(PermissionKeys.ModerationMachineBan))
            return;
        var targetClient = _clientManager.GetClientByUserId(userId);
        var habbo = targetClient?.GetHabbo();
        if (habbo == null)
        {
            session.SendWhisper("An error occoured whilst finding that user in the database.");
            return;
        }
        if (!session.GetHabbo().Access.Outranks(habbo.Access))
        {
            session.SendWhisper("Oops, you cannot ban that user.");
            return;
        }
        var expiresAt = _clock.GetUtcNow().AddHours(hours);
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
        var machineId = targetClient!.MachineId;
#pragma warning restore CS0618
        // Fail closed first, from memory; the account, its recorded address and its device then share one deadline.
        targetClient.Disconnect();
        await _moderationManager.BanAccount(session.GetHabbo().Username, habbo.Id, habbo.Username, message ?? "No reason specified.", expiresAt, deadline.Token,
            includeAddress: ipBan || machineBan, machineId: machineBan ? machineId : null);
    }
}
