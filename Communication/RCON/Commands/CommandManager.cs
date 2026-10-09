namespace Plus.Communication.RCON.Commands;

public class CommandManager : ICommandManager
{
    /// <summary>
    /// Commands registered for use.
    /// </summary>
    private readonly Dictionary<string, IRconCommand> _commands;

    /// <summary>
    /// The default initializer for the CommandManager
    /// </summary>
    public CommandManager(IEnumerable<IRconCommand> commands)
    {
        _commands = commands.ToDictionary(command => command.Key);
    }

    /// <summary>
    /// Request the text to parse and check for commands that need to be executed.
    /// </summary>
    /// <param name="data">A string of data split by char(1), the first part being the command and the second part being the parameters.</param>
    /// <returns>True if parsed or false if not.</returns>
    public bool Parse(string data)
    {
        if (data.Length == 0 || string.IsNullOrEmpty(data)) {
            return false;
        }

        var parts = data.Split(Convert.ToChar(1));

        if (_commands.TryGetValue(parts[0].ToLower(), out var command)) {
            var parameters = parts[1].Split(':');

            return command.TryExecute(parameters).Result;
        }

        return false;
    }

    public IAcknowledgedRconCommand? Acknowledged(string command) =>
        _commands.TryGetValue(command, out var registered) ? registered as IAcknowledgedRconCommand : null;
}
