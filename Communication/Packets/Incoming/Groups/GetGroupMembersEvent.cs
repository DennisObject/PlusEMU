using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class GetGroupMembersEvent : IPacketEvent
{
    private readonly IGroupManager _groupManager;
    private readonly ICacheManager _cacheManager;

    public GetGroupMembersEvent(IGroupManager groupManager, ICacheManager cacheManager)
    {
        _groupManager = groupManager;
        _cacheManager = cacheManager;
    }
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var page = packet.ReadInt();
        var searchVal = packet.ReadString();
        var requestType = packet.ReadInt();
        if (!_groupManager.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        var canManage = group.CreatorId == session.GetHabbo().Id || group.IsAdmin(session.GetHabbo().Id);
        if (!canManage && requestType >= 2)
            requestType = 0;
        var members = new List<CachedUser>();
        switch (requestType)
        {
            case 0:
            {
                var memberIds = group.GetAllMembers;
                foreach (var id in memberIds.ToList())
                {
                    var groupMember = _cacheManager.GenerateUser(id);
                    if (groupMember == null)
                        continue;
                    if (!members.Contains(groupMember))
                        members.Add(groupMember);
                }
                break;
            }
            case 1:
            {
                var adminIds = group.GetAdministrators;
                foreach (var id in adminIds.ToList())
                {
                    var groupMember = _cacheManager.GenerateUser(id);
                    if (groupMember == null)
                        continue;
                    if (!members.Contains(groupMember))
                        members.Add(groupMember);
                }
                break;
            }
            case 2:
            {
                var requestIds = group.GetRequests;
                foreach (var id in requestIds.ToList())
                {
                    var groupMember = _cacheManager.GenerateUser(id);
                    if (groupMember == null)
                        continue;
                    if (!members.Contains(groupMember))
                        members.Add(groupMember);
                }
                break;
            }
        }
        if (!string.IsNullOrEmpty(searchVal))
            members = members.Where(x => x.Username.StartsWith(searchVal, StringComparison.OrdinalIgnoreCase)).ToList();
        const int pageSize = 14;
        var startIndex = Math.Max(page, 0) * pageSize;
        session.Send(new GroupMembersComposer(group, members.Skip(startIndex).Take(pageSize).ToList(), members.Count, page, canManage, requestType, searchVal));
        return Task.CompletedTask;
    }
}