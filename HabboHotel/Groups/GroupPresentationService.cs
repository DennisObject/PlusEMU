using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Core.Settings;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.GameClients;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

public sealed record GroupMembersRequest(int GroupId, int Page, string Search, int RequestType);

public sealed record GroupMemberPresentation(int Role, int Id, string Username, string Look);

public sealed record GroupMembersPresentation(
    int GroupId,
    string GroupName,
    uint RoomId,
    string Badge,
    int Total,
    ImmutableArray<GroupMemberPresentation> Members,
    bool CanManage,
    int Page,
    int RequestType,
    string Search);

public sealed record BadgePartPresentation(int Id, string AssetOne, string AssetTwo);

public sealed record BadgeColourPresentation(int Id, string Colour);

public sealed record BadgeEditorPresentation(
    ImmutableArray<BadgePartPresentation> Bases,
    ImmutableArray<BadgePartPresentation> Symbols,
    ImmutableArray<BadgeColourPresentation> BaseColours,
    ImmutableArray<BadgeColourPresentation> SymbolColours,
    ImmutableArray<BadgeColourPresentation> BackgroundColours);

public sealed record GroupCreationRoom(uint Id, string Name);

public sealed record GroupCreationPresentation(int Price, ImmutableArray<GroupCreationRoom> Rooms);

[Singleton]
public interface IGroupPresentationService
{
    void ShowMembers(GameClient session, GroupMembersRequest request);
    void ShowBadgeEditor(GameClient session);
    void ShowCreationWindow(GameClient session);
    void ShowInfo(GameClient session, int groupId, bool newWindow);
    void ShowFurnitureSettings(GameClient session, uint itemId, int groupId);
}

public sealed class GroupPresentationService(IGroupManager groups, ICacheManager cache, IRoomDataLoader rooms, ISettingsManager settings, IGroupInfoSnapshotService groupInfo) : IGroupPresentationService
{
    private const int PageSize = 14;

    public void ShowInfo(GameClient session, int groupId, bool newWindow)
    {
        if (groups.TryGetGroup(groupId, out var group))
            session.Send(new GroupInfoComposer(groupInfo.Capture(group, session.GetHabbo().Id), newWindow));
    }

    public void ShowFurnitureSettings(GameClient session, uint itemId, int groupId)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        if (!habbo.InRoom || room == null)
            return;
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.GuildGate ||
            !groups.TryGetGroup(groupId, out var group))
            return;
        var settings = GroupFurniSettingsSnapshot.Capture(group, itemId, habbo.Id);
        var info = groupInfo.Capture(group, habbo.Id);
        session.Send(new GroupFurniSettingsComposer(settings));
        session.Send(new GroupInfoComposer(info));
    }

    public void ShowMembers(GameClient session, GroupMembersRequest request)
    {
        if (!groups.TryGetGroup(request.GroupId, out var group))
            return;

        var actorId = session.GetHabbo().Id;
        var canManage = group.CreatorId == actorId || group.IsAdmin(actorId);
        var requestType = !canManage && request.RequestType >= 2 ? 0 : request.RequestType;
        var users = ResolveUsers(group, requestType);
        if (!string.IsNullOrEmpty(request.Search))
        {
            users = users
                .Where(user => user.Username.StartsWith(request.Search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var total = users.Count;
        var offset = request.Page <= 0 ? 0L : (long)request.Page * PageSize;
        var page = offset >= total
            ? []
            : users.Skip((int)offset).Take(PageSize).Select(user => CaptureMember(group, user)).ToImmutableArray();
        var presentation = new GroupMembersPresentation(
            group.Id,
            group.Name,
            group.RoomId,
            group.Badge,
            total,
            page,
            canManage,
            request.Page,
            requestType,
            request.Search);
        session.Send(new GroupMembersComposer(presentation));
    }

    public void ShowBadgeEditor(GameClient session)
    {
        var presentation = new BadgeEditorPresentation(
            groups.BadgeBases.Select(CapturePart).ToImmutableArray(),
            groups.BadgeSymbols.Select(CapturePart).ToImmutableArray(),
            groups.BadgeBaseColours.Select(CaptureColour).ToImmutableArray(),
            groups.BadgeSymbolColours.Select(CaptureColour).ToImmutableArray(),
            groups.BadgeBackColours.Select(CaptureColour).ToImmutableArray());
        session.Send(new BadgeEditorPartsComposer(presentation));
    }

    public void ShowCreationWindow(GameClient session)
    {
        var availableRooms = rooms.GetRoomsDataByOwnerSortByName(session.GetHabbo().Id)
            .Where(room => room.Group == null)
            .Select(room => new GroupCreationRoom(room.Id, room.Name))
            .ToImmutableArray();
        var price = Convert.ToInt32(settings.TryGetValue("catalog.group.purchase.cost"));
        session.Send(new GroupCreationWindowComposer(new GroupCreationPresentation(price, availableRooms)));
    }

    private List<CachedUser> ResolveUsers(Group group, int requestType)
    {
        var ids = requestType switch
        {
            0 => group.GetAllMembers,
            1 => group.GetAdministrators,
            2 => group.GetRequests,
            _ => []
        };
        var users = new List<CachedUser>();
        foreach (var id in ids)
        {
            var user = cache.GenerateUser(id);
            if (user != null && !users.Contains(user))
                users.Add(user);
        }
        return users;
    }

    private static GroupMemberPresentation CaptureMember(Group group, CachedUser user) => new(
        group.CreatorId == user.Id ? 0 : group.IsAdmin(user.Id) ? 1 : group.IsMember(user.Id) ? 2 : 3,
        user.Id,
        user.Username,
        user.Look);

    private static BadgePartPresentation CapturePart(GroupBadgeParts part) =>
        new(part.Id, part.AssetOne, part.AssetTwo);

    private static BadgeColourPresentation CaptureColour(GroupColours colour) =>
        new(colour.Id, colour.Colour);
}
