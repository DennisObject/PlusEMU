using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class ProgressUserAchievementCommand : IRconCommand
{
    private readonly IGameClientManager _gameClientManager;
    private readonly IAchievementManager _achievementManager;
    public string Description => "This command is used to progress a users achievement.";

    public string Key => "progress_user_achievement";
    public string Parameters => "%userId% %achievement% %progess%";

    public ProgressUserAchievementCommand(IGameClientManager gameClientManager, IAchievementManager achievementManager)
    {
        _gameClientManager = gameClientManager;
        _achievementManager = achievementManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (!int.TryParse(parameters[0], out var userId))
        {
            return Task.FromResult(false);
        }

        var client = _gameClientManager.GetClientByUserId(userId);

        if (client == null || client.GetHabbo() == null)
        {
            return Task.FromResult(false);
        }

        // Validate the achievement
        if (string.IsNullOrEmpty(Convert.ToString(parameters[1])))
        {
            return Task.FromResult(false);
        }

        var achievement = Convert.ToString(parameters[1]);

        // Validate the progress
        if (!int.TryParse(parameters[2], out var progress))
        {
            return Task.FromResult(false);
        }

        _achievementManager.ProgressAchievement(client, achievement, progress);

        return Task.FromResult(true);
    }
}
