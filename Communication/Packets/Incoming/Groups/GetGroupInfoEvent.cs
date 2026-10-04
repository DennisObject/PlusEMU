using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class GetGroupInfoEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IGroupInfoSnapshotService _groupInfo;

    public GetGroupInfoEvent(IGroupManager groupManager, IGroupInfoSnapshotService groupInfo)
    {
        _groupInfo = groupInfo;
        _groupManager = groupManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var newWindow = packet.ReadBool();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        session.Send(new GroupInfoComposer(_groupInfo.Capture(group, session.GetHabbo().Id), newWindow));
        return Task.CompletedTask;
    }
}