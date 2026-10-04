using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Dapper;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class UpdateGroupBadgeEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IGroupInfoSnapshotService _groupInfo;
    private readonly IDatabase _database;

    public UpdateGroupBadgeEvent(IGroupManager groupManager, IDatabase database, IGroupInfoSnapshotService groupInfo)
    {
        _groupInfo = groupInfo;
        _groupManager = groupManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        if (group.CreatorId != session.GetHabbo().Id)
            return Task.CompletedTask;
        var count = packet.ReadInt();
        if (count < 3 || count > 15 || count % 3 != 0 || packet.Buffer.Length != count * sizeof(int))
            return Task.CompletedTask;
        var badge = "";
        for (var i = 0; i < count / 3; i++) badge += BadgePartUtility.WorkBadgeParts(i == 0, packet.ReadInt().ToString(), packet.ReadInt().ToString(), packet.ReadInt().ToString());
        group.Badge = string.IsNullOrWhiteSpace(badge) ? "b05114s06114" : badge;
        using (var connection = _database.Connection())
        {
            connection.Execute("UPDATE `groups` SET `badge` = @badge WHERE `id` = @groupId LIMIT 1", new { badge = group.Badge, groupId = group.Id });
        }
        session.Send(new GroupInfoComposer(_groupInfo.Capture(group, session.GetHabbo().Id)));
        return Task.CompletedTask;
    }
}