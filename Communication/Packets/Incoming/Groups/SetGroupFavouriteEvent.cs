using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Dapper;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class SetGroupFavouriteEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly IDatabase _database;

    public SetGroupFavouriteEvent(IGroupManager groupManager, IDatabase database)
    {
        _groupManager = groupManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        if (groupId == 0)
            return Task.CompletedTask;
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        session.GetHabbo().HabboStats.FavouriteGroupId = group.Id;
        using (var connection = _database.Connection())
        {
            connection.Execute("UPDATE `user_statistics` SET `groupid` = @groupId WHERE `id` = @userId LIMIT 1",
                new { groupId = session.GetHabbo().HabboStats.FavouriteGroupId, userId = session.GetHabbo().Id });
        }
        if (session.GetHabbo().InRoom && session.GetHabbo().CurrentRoom != null)
        {
            session.GetHabbo().CurrentRoom.SendPacket(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
            session.GetHabbo().CurrentRoom.SendPacket(new HabboGroupBadgesComposer([new(group.Id, group.Badge)]));
            var user = session.GetHabbo().CurrentRoom.GetRoomUserManager()
                .GetRoomUserByHabbo(session.GetHabbo().Id);
            if (user != null)
                session.GetHabbo().CurrentRoom.SendPacket(new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(group, user.VirtualId)));
        }
        else
            session.Send(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
        return Task.CompletedTask;
    }
}