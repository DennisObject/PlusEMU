using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Groups;
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

[Singleton]
public interface IGroupPresentationService
{
    void ShowMembers(GameClient session, GroupMembersRequest request);
    void ShowBadgeEditor(GameClient session);
}

public sealed class GroupPresentationService(IGroupManager groups, ICacheManager cache) : IGroupPresentationService
{
    private const int PageSize = 14;

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
