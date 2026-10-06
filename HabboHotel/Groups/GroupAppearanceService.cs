using System.Collections.Immutable;
using System.Globalization;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

public readonly record struct GroupIdentityRequest(int GroupId, string Name, string Description);
public readonly record struct GroupBadgePartRequest(int Symbol, int Colour, int Position);
public readonly record struct GroupColoursRequest(int GroupId, int MainColour, int SecondaryColour);

[Singleton]
public interface IGroupAppearanceService
{
    Task UpdateIdentity(GameClient session, GroupIdentityRequest request);
    Task UpdateBadge(GameClient session, int groupId, ImmutableArray<GroupBadgePartRequest> parts);
    Task UpdateColours(GameClient session, GroupColoursRequest request);
}

public sealed class GroupAppearanceService(
    IGroupManager groups,
    IWordFilterManager wordFilter,
    IGroupInfoSnapshotService groupInfo,
    IGroupAppearanceStore store) : IGroupAppearanceService
{
    public Task UpdateIdentity(GameClient session, GroupIdentityRequest request)
    {
        var name = wordFilter.CheckMessage(request.Name);
        var description = wordFilter.CheckMessage(request.Description);

        if (!TryGetOwnedGroup(session, request.GroupId, out var group))
        {
            return Task.CompletedTask;
        }

        lock (group)
        {
            if (!store.UpdateIdentity(group.Id, name, description))
            {
                return Task.CompletedTask;
            }

            group.Name = name;
            group.Description = description;
            SendGroupInfo(session, group);
        }

        return Task.CompletedTask;
    }

    public Task UpdateBadge(GameClient session, int groupId, ImmutableArray<GroupBadgePartRequest> parts)
    {
        if (!TryGetOwnedGroup(session, groupId, out var group) || !TryBuildBadge(parts, out var badge))
        {
            return Task.CompletedTask;
        }

        lock (group)
        {
            if (!store.UpdateBadge(group.Id, badge))
            {
                return Task.CompletedTask;
            }

            group.Badge = badge;
            SendGroupInfo(session, group);
        }

        return Task.CompletedTask;
    }

    public Task UpdateColours(GameClient session, GroupColoursRequest request)
    {
        if (!TryGetOwnedGroup(session, request.GroupId, out var group))
        {
            return Task.CompletedTask;
        }

        lock (group)
        {
            if (!store.UpdateColours(group.Id, request.MainColour, request.SecondaryColour))
            {
                return Task.CompletedTask;
            }

            group.Colour1 = request.MainColour;
            group.Colour2 = request.SecondaryColour;
            SendGroupInfo(session, group);
            PublishGuildItems(session, group.Id);
        }

        return Task.CompletedTask;
    }

    private bool TryGetOwnedGroup(GameClient session, int groupId, out Group group)
    {
        if (!groups.TryGetGroup(groupId, out var found) || found.CreatorId != session.GetHabbo().Id)
        {
            group = null!;

            return false;
        }

        group = found;

        return true;
    }

    private void SendGroupInfo(GameClient session, Group group) =>
        session.Send(new GroupInfoComposer(groupInfo.Capture(group, session.GetHabbo().Id)));

    private static bool TryBuildBadge(ImmutableArray<GroupBadgePartRequest> parts, out string badge)
    {
        badge = string.Empty;

        if (parts.IsDefaultOrEmpty || parts.Length > 5)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            badge += BadgePartUtility.WorkBadgeParts(i == 0,
                part.Symbol.ToString(CultureInfo.InvariantCulture),
                part.Colour.ToString(CultureInfo.InvariantCulture),
                part.Position.ToString(CultureInfo.InvariantCulture));
        }

        if (string.IsNullOrWhiteSpace(badge))
        {
            badge = "b05114s06114";
        }

        return true;
    }

    private static void PublishGuildItems(GameClient session, int groupId)
    {
        var room = session.GetHabbo().CurrentRoom;

        if (room == null)
        {
            return;
        }

        var snapshots = room.GetRoomItemHandler().GetFloor
            .Where(item => item.GroupId == groupId && item.Definition.InteractionType is
                InteractionType.GuildItem or InteractionType.GuildGate or InteractionType.GuildForum)
            .Select(RoomItemSnapshot.Capture)
            .ToImmutableArray();

        foreach (var snapshot in snapshots)
        {
            room.SendPacket(new ObjectUpdateComposer(snapshot));
        }
    }
}
