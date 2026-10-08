using Plus.HabboHotel.Users.Grants;

namespace Plus.Communication.RCON.Commands;

/// <summary>
/// A command that answers acknowledged requests with a stable outcome id and a machine-readable result instead of a bare
/// success flag. It validates its own parameters, so acknowledged requests may give it parameters up to the request cap.
/// </summary>
public interface IAcknowledgedRconCommand : IRconCommand
{
    Task<GrantOutcome> Execute(string[] parameters);

    async Task<bool> IRconCommand.TryExecute(string[] parameters) => (await Execute(parameters)).Succeeded;
}
