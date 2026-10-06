namespace Plus.Communication.RCON.Commands;

public interface ICommandManager
{
    /// <summary>
    /// Request the text to parse and check for commands that need to be executed.
    /// </summary>
    /// <param name="data">A string of data split by char(1), the first part being the command and the second part being the parameters.</param>
    /// <returns>True if parsed or false if not.</returns>
    bool Parse(string data);
}
