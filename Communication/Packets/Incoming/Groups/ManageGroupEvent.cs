using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class ManageGroupEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;

    public ManageGroupEvent(IGroupManager groupManager)
    {
        _groupManager = groupManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        if (group.CreatorId != session.GetHabbo().Id && !session.GetHabbo().Access.Can(PermissionKeys.GroupManagementOverride))
            return Task.CompletedTask;
        session.Send(new ManageGroupComposer(group, group.Badge.Replace("b", "").Split('s')));
        return Task.CompletedTask;
    }
}