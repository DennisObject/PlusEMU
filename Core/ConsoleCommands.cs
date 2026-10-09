using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;

namespace Plus.Core;

public static class ConsoleCommands
{
    private static ILogger? _logger;
    private static ILogger Logger => _logger ?? throw new InvalidOperationException("Configure logging before use.");
    private static IGameClientManager? _clients;
    private static IGameClientManager Clients => _clients ?? throw new InvalidOperationException("Configure console commands before use.");

    public static void Configure(ILoggerFactory loggerFactory, IGameClientManager clients)
    {
        _logger = loggerFactory.CreateLogger(typeof(ConsoleCommands).FullName!);
        _clients = clients;
    }

    public static void InvokeCommand(string inputData)
    {
        if (string.IsNullOrEmpty(inputData)) {
            return;
        }

        try {
            var parameters = inputData.Split(' ');

            switch (parameters[0].ToLower()) {
                case "stop":
                case "shutdown": {
                        Logger.LogWarning("The server is saving users furniture, rooms, etc. WAIT FOR THE SERVER TO CLOSE, DO NOT EXIT THE PROCESS IN TASK MANAGER!!");
                        PlusEnvironment.PerformShutDown();
                        break;
                    }
                case "alert": {
                        var notice = inputData.Substring(6);
                        Clients.SendPacket(new BroadcastMessageAlertComposer($"{PlusEnvironment.LanguageManager.TryGetValue("server.console.alert")}\n\n{notice}"));
                        Logger.LogInformation("Alert successfully sent.");
                        break;
                    }
                default: {
                        Logger.LogError("{Command} is an unknown or unsupported command. Type help for more information", parameters[0].ToLower());
                        break;
                    }
            }
        }
        catch (Exception e) {
            Logger.LogError(e, "Error in command [{Input}]", inputData);
        }
    }
}
