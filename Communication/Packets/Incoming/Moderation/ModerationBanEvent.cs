using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.Utilities;

namespace Plus.Communication.Packets.Incoming.Moderation;

internal class ModerationBanEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;
    private readonly IModerationManager _moderationManager;

    public ModerationBanEvent(IGameClientManager clientManager, IModerationManager moderationManager)
    {
        _clientManager = clientManager;
        _moderationManager = moderationManager;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        if (!session.GetHabbo().Permissions.HasRight("mod_soft_ban"))
            return;
        var userId = packet.ReadInt();
        var message = packet.ReadString();
        var length = packet.ReadInt() * 3600 + BanClock.Now();
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
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
        var machineId = targetClient!.MachineId;
#pragma warning restore CS0618
        // Fail closed first, from memory; the account, its recorded address and its device then share one deadline.
        targetClient.Disconnect();
        await _moderationManager.BanAccount(session.GetHabbo().Username, habbo.Id, habbo.Username, message ?? "No reason specified.", length, deadline.Token,
            includeAddress: ipBan || machineBan, machineId: machineBan ? machineId : null);
    }
}
