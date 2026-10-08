using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Users.Authentication.Tasks;

public class DisconnectCurrentOnlineHabboTask : IAuthenticationTask
{
    private readonly IGameClientManager _gameClientManager;

    public DisconnectCurrentOnlineHabboTask(IGameClientManager gameClientManager)
    {
        _gameClientManager = gameClientManager;
    }

    public Task<bool> CanLogin(int userId)
    {
        var existingSession = _gameClientManager.GetClientByUserId(userId);
        existingSession?.Disconnect();

        // The replaced session logs out later on the thread pool, outside the session gate. Saving and closing its wallet here,
        // while this login holds the gate, keeps that late logout from overwriting what this login loads or what an offline
        // grant writes after it; the logout then finds the wallet saved and skips its own save.
        existingSession?.GetHabbo()?.Save();

        return Task.FromResult(true);
    }
}
