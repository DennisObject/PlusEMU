using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class AcceptGroupMembershipEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;

    public AcceptGroupMembershipEvent(IGroupManager groupManager)
    {
        _groupManager = groupManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var userId = packet.ReadInt();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        if (session.GetHabbo().Id != group.CreatorId && !group.IsAdmin(session.GetHabbo().Id) && !session.GetHabbo().Permissions.HasRight("fuse_group_accept_any"))
            return Task.CompletedTask;
        if (!group.HasRequest(userId))
            return Task.CompletedTask;
        var habbo = PlusEnvironment.GetHabboById(userId);
        group.HandleRequest(userId, true);
        if (habbo != null)
            session.Send(new GroupMemberUpdatedComposer(groupId, habbo, 2));
        else
            session.Send(new UnknownGroupComposer(group.Id, userId));
        return Task.CompletedTask;
    }
}