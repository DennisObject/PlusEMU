using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

/// <summary>The one-time password is returned only in the acting operator's action result.</summary>
[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingResetUserPasswordEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingUserActions _users;

    public HousekeepingResetUserPasswordEvent(IHousekeepingActionRunner runner, IHousekeepingUserActions users)
    {
        _runner = runner;
        _users = users;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        _runner.Run(session, "user.reset_password", HousekeepingRights.Password, actor => _users.ResetPassword(actor, userId));

        return Task.CompletedTask;
    }
}
