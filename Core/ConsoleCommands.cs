using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Outgoing.Moderation;

namespace Plus.Core;

public static class ConsoleCommands
{
    private static ILogger _logger = NullLogger.Instance;

    public static void Configure(ILoggerFactory loggerFactory) => _logger = loggerFactory.CreateLogger(typeof(ConsoleCommands).FullName!);

    public static void InvokeCommand(string inputData)
    {
        if (string.IsNullOrEmpty(inputData))
            return;
        try
        {
            var parameters = inputData.Split(' ');
            switch (parameters[0].ToLower())
            {
                case "stop":
                case "shutdown":
                {
                    _logger.LogWarning("The server is saving users furniture, rooms, etc. WAIT FOR THE SERVER TO CLOSE, DO NOT EXIT THE PROCESS IN TASK MANAGER!!");
                    PlusEnvironment.PerformShutDown();
                    break;
                }
                case "alert":
                {
                    var notice = inputData.Substring(6);
                    PlusEnvironment.Game.ClientManager
                        .SendPacket(new BroadcastMessageAlertComposer($"{PlusEnvironment.LanguageManager.TryGetValue("server.console.alert")}\n\n{notice}"));
                    _logger.LogInformation("Alert successfully sent.");
                    break;
                }
                default:
                {
                    _logger.LogError("{Command} is an unknown or unsupported command. Type help for more information", parameters[0].ToLower());
                    break;
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in command [{Input}]", inputData);
        }
    }
}
