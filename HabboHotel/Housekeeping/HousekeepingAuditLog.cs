using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Housekeeping;

public sealed class HousekeepingAuditEntry
{
    public int Id { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public int ActorId { get; init; }
    public string ActorName { get; init; } = string.Empty;
    public string TargetType { get; init; } = "user";
    public int TargetId { get; init; }
    public string TargetLabel { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool Success { get; init; }
}

public interface IHousekeepingAuditLog
{
    void Write(int actorId, string actorName, string action, HousekeepingOutcome outcome);
    IReadOnlyList<HousekeepingAuditEntry> List(int limit);
}

public sealed class HousekeepingAuditLog : IHousekeepingAuditLog
{
    private readonly IDatabase _database;
    private readonly TimeProvider _clock;

    public HousekeepingAuditLog(IDatabase database, TimeProvider clock)
    {
        _database = database;
        _clock = clock;
    }

    public void Write(int actorId, string actorName, string action, HousekeepingOutcome outcome)
    {
        var createdAt = _clock.GetUtcNow().UtcDateTime;
        using var connection = _database.Connection();
        connection.Execute(
            "INSERT INTO `housekeeping_log` (`timestamp`, `actor_id`, `actor_name`, `target_type`, `target_id`, `target_label`, `action`, `detail`, `success`) " +
            "VALUES (@timestamp, @actorId, @actorName, @targetType, @targetId, @targetLabel, @action, @detail, @success)",
            new
            {
                timestamp = createdAt,
                actorId,
                actorName,
                targetType = TargetTypeName(outcome.Target.Type),
                targetId = outcome.Target.Id,
                targetLabel = outcome.Target.Label,
                action,
                detail = HousekeepingLimits.AuditValue(outcome.Detail),
                success = outcome.Ok
            });
    }

    public IReadOnlyList<HousekeepingAuditEntry> List(int limit)
    {
        using var connection = _database.Connection();

        return connection.Query<HousekeepingAuditEntry>(
            "SELECT `id`, `timestamp` AS CreatedAt, `actor_id` AS ActorId, `actor_name` AS ActorName, `target_type` AS TargetType, `target_id` AS TargetId, " +
            "`target_label` AS TargetLabel, `action`, `detail`, `success` FROM `housekeeping_log` ORDER BY `id` DESC LIMIT @limit",
            new { limit = Math.Clamp(limit, 1, HousekeepingLimits.MaxActionLogEntries) }).ToList();
    }

    internal static string TargetTypeName(HousekeepingTargetType type) => type switch
    {
        HousekeepingTargetType.Room => "room",
        HousekeepingTargetType.Hotel => "hotel",
        _ => "user"
    };
}
