using System.Collections.Immutable;
using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

public sealed record GroupMembershipSnapshot(ImmutableArray<int> Members, ImmutableArray<int> Administrators,
    ImmutableArray<int> Requests)
{
    public static GroupMembershipSnapshot Empty { get; } = new([], [], []);
    public static GroupMembershipSnapshot ForOwner(int ownerId) => new([], [ownerId], []);
}

[Singleton]
public interface IGroupMembershipLoader
{
    GroupMembershipSnapshot Load(int groupId);
}

public sealed class GroupMembershipLoader(IDatabase database) : IGroupMembershipLoader
{
    public GroupMembershipSnapshot Load(int groupId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var memberships = connection.Query<MembershipRow>("""
            SELECT user_id AS UserId, `rank` <> '0' AS IsAdministrator
            FROM group_memberships WHERE group_id = @groupId ORDER BY id
            """, new
        {
            groupId
        }, transaction).ToArray();
        var memberIds = memberships.Where(row => !row.IsAdministrator).Select(row => checked((int)row.UserId)).Distinct().ToImmutableArray();
        var administratorIds = memberships.Where(row => row.IsAdministrator).Select(row => checked((int)row.UserId)).Distinct().ToImmutableArray();
        var participantIds = memberIds.Concat(administratorIds).ToHashSet();
        var requests = connection.Query<uint>("""
            SELECT user_id FROM group_requests WHERE group_id = @groupId ORDER BY user_id
            """, new
        {
            groupId
        }, transaction).Select(userId => checked((int)userId)).Distinct().ToArray();
        var staleRequests = requests.Where(participantIds.Contains).ToArray();

        if (staleRequests.Length > 0)
        {
            connection.Execute("""
                DELETE FROM group_requests WHERE group_id = @groupId AND user_id IN @staleRequests
                """, new
            {
                groupId,
                staleRequests
            }, transaction);
        }

        transaction.Commit();

        return new(memberIds, administratorIds, requests.Where(userId => !participantIds.Contains(userId)).ToImmutableArray());
    }

    private sealed class MembershipRow
    {
        public uint UserId
        {
            get; set;
        }
        public bool IsAdministrator
        {
            get; set;
        }
    }
}
