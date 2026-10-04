using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class GetGroupFurniSettingsEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IGroupInfoSnapshotService _groupInfo;

    public GetGroupFurniSettingsEvent(IGroupManager groupManager, IGroupInfoSnapshotService groupInfo)
    {
        _groupInfo = groupInfo;
        _groupManager = groupManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var groupId = packet.ReadInt();
        var item = session.GetHabbo().CurrentRoom.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary)
            return Task.CompletedTask;
        if (item.Definition.InteractionType != InteractionType.GuildGate)
            return Task.CompletedTask;
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        session.Send(new GroupFurniSettingsComposer(group, itemId, session.GetHabbo().Id));
        session.Send(new GroupInfoComposer(_groupInfo.Capture(group, session.GetHabbo().Id)));
        return Task.CompletedTask;
    }
}