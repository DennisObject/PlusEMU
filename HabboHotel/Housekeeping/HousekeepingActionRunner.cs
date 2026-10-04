using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Housekeeping;

public interface IHousekeepingActionRunner
{
    /// <summary>True when the session may use the panel at all; other sessions get no reply.</summary>
    bool HasAccess(GameClient session);

    /// <summary>
    /// Authorizes, runs and audits one staff mutation, then acknowledges it with the exact action key.
    /// </summary>
    void Run(GameClient session, string actionKey, string right, Func<Habbo, HousekeepingOutcome> action);

    /// <inheritdoc cref="Run"/>
    Task RunAsync(GameClient session, string actionKey, string right, Func<Habbo, Task<HousekeepingOutcome>> action);
}

public sealed class HousekeepingActionRunner : IHousekeepingActionRunner
{
    private const string Failed = "housekeeping.error.db_failed";
    private readonly IHousekeepingAuditLog _auditLog;
    private readonly ILogger<HousekeepingActionRunner> _logger;

    public HousekeepingActionRunner(IHousekeepingAuditLog auditLog, ILogger<HousekeepingActionRunner> logger)
    {
        _auditLog = auditLog;
        _logger = logger;
    }

    public bool HasAccess(GameClient session) => session.GetHabbo()?.Access?.Can(HousekeepingRights.Access) == true;

    // The synchronous action completes inline, so this never blocks on pending work.
    public void Run(GameClient session, string actionKey, string right, Func<Habbo, HousekeepingOutcome> action) =>
        RunAsync(session, actionKey, right, actor => Task.FromResult(action(actor))).GetAwaiter().GetResult();

    public async Task RunAsync(GameClient session, string actionKey, string right, Func<Habbo, Task<HousekeepingOutcome>> action)
    {
        if (!HasAccess(session)) return;
        var actor = session.GetHabbo();
        var outcome = actor.Access.Can(right) ? await Execute(actor, actionKey, action) : HousekeepingOutcome.Fail(HousekeepingErrors.Forbidden, HousekeepingTarget.Hotel);
        _auditLog.Write(actor.Id, actor.Username, actionKey, outcome);
        session.Send(new HousekeepingActionResultComposer(actionKey, outcome.Ok, outcome.Ok ? outcome.ActionId : 0, outcome.Message));
    }

    private async Task<HousekeepingOutcome> Execute(Habbo actor, string actionKey, Func<Habbo, Task<HousekeepingOutcome>> action)
    {
        try
        {
            return await action(actor);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Housekeeping action {Action} by {ActorId} failed", actionKey, actor.Id);
            return HousekeepingOutcome.Fail(Failed, HousekeepingTarget.Hotel);
        }
    }
}
