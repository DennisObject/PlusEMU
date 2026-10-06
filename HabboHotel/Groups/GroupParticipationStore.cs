using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

public enum GroupJoinOutcome
{
    Inserted,
    AlreadyPresent,
    LimitReached,
    Refused
}

[Singleton]
public interface IGroupParticipationStore
{
    GroupJoinOutcome Join(int userId, int groupId, bool request, int membershipLimit);
    bool SaveFavourite(int userId, int groupId);
}

// Every write runs under the account row lock, so joins and favourite writes for one user serialize across groups.
public sealed class GroupParticipationStore(IDatabase database) : IGroupParticipationStore
{
    public GroupJoinOutcome Join(int userId, int groupId, bool request, int membershipLimit)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (!LockAccount(connection, transaction, userId) || !LockGroup(connection, transaction, groupId))
            return GroupJoinOutcome.Refused;
        var memberships = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM group_memberships AS m INNER JOIN `groups` AS g ON m.group_id = g.id WHERE m.user_id = @userId",
            new { userId }, transaction);
        if (memberships >= membershipLimit)
            return GroupJoinOutcome.LimitReached;
        // The tables have no unique keys, so presence is checked under the lock before each insert.
        var outcome = request
            ? InsertRequest(connection, transaction, userId, groupId)
            : InsertMembership(connection, transaction, userId, groupId);
        transaction.Commit();
        return outcome;
    }

    public bool SaveFavourite(int userId, int groupId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (!LockAccount(connection, transaction, userId) || groupId != 0 && !LockGroup(connection, transaction, groupId))
            return false;
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_statistics WHERE id = @userId", new { userId }, transaction) != 1)
            return false;
        connection.Execute("UPDATE user_statistics SET groupid = @groupId WHERE id = @userId", new { userId, groupId }, transaction);
        transaction.Commit();
        return true;
    }

    // Deletion locks the same groups row, so a deleted group cannot gain a membership, request or favourite afterwards.
    private static bool LockGroup(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int groupId) =>
        connection.Query<int>("SELECT id FROM `groups` WHERE id = @groupId FOR UPDATE", new { groupId }, transaction).Count() == 1;

    private static bool LockAccount(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int userId) =>
        connection.Query<int>("SELECT id FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction).Count() == 1;

    private static GroupJoinOutcome InsertRequest(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int userId, int groupId)
    {
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests WHERE user_id = @userId AND group_id = @groupId", new { userId, groupId }, transaction) > 0)
            return GroupJoinOutcome.AlreadyPresent;
        connection.Execute("INSERT INTO group_requests (user_id, group_id) VALUES (@userId, @groupId)", new { userId, groupId }, transaction);
        return GroupJoinOutcome.Inserted;
    }

    private static GroupJoinOutcome InsertMembership(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, int userId, int groupId)
    {
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships WHERE user_id = @userId AND group_id = @groupId", new { userId, groupId }, transaction) > 0)
            return GroupJoinOutcome.AlreadyPresent;
        connection.Execute("INSERT INTO group_memberships (user_id, group_id) VALUES (@userId, @groupId)", new { userId, groupId }, transaction);
        return GroupJoinOutcome.Inserted;
    }
}
