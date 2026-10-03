using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.UserData;
using System.Diagnostics;

namespace Plus.HabboHotel.Users.Authentication;

internal class Authenticator : IAuthenticator
{
    private readonly IEnumerable<IAuthenticationTask> _authenticationTasks;
    private readonly IGameClientManager _gameClientManager;
    private readonly IUserDataFactory _userDataFactory;
    private readonly IDatabase _database;
    private readonly IAccountSessionGate _sessionGate;

    public Authenticator(IEnumerable<IAuthenticationTask> authenticationTasks, IGameClientManager gameClientManager, IUserDataFactory userDataFactory, IDatabase database,
        IAccountSessionGate sessionGate)
    {
        _authenticationTasks = authenticationTasks;
        _gameClientManager = gameClientManager;
        _userDataFactory = userDataFactory;
        _database = database;
        _sessionGate = sessionGate;
    }

    public async Task<AuthenticationError?> AuthenticateUsingSSO(GameClient session, string sso)
    {
        var started = _sessionGate.Begin();
        sso = sso.Trim();
        if (string.IsNullOrEmpty(sso))
            return AuthenticationError.EmptySSO;

        if (!Debugger.IsAttached && sso.Length < 15)
            return AuthenticationError.InvalidSSO;

        var userId = await GetUserIdFromSso(sso);
        if (userId == default)
            return AuthenticationError.NoAccountFound;

        if (!Debugger.IsAttached)
            await ResetSso(userId);

        Habbo? habbo;
        // Staff writes to this account wait until the session is registered, so they never land under a stale load.
        using (await _sessionGate.EnterAsync(userId))
        {
            // A password reset after this ticket was resolved revokes the login.
            if (_sessionGate.IsRevoked(userId, started))
                return AuthenticationError.LoginProhibited;

            var canLogin = await CanLogin(userId);
            if (!canLogin)
                return AuthenticationError.LoginProhibited;

            habbo = await _userDataFactory.Create(userId);
            if (habbo == null)
                return AuthenticationError.NoAccountFound;

            habbo.Disconnected += async (_, _) => await OnHabboDisconnected(habbo);

            session.SetHabbo(habbo);

            // TODO @80O: Remove after splitting up
            habbo.Init(session);
            _gameClientManager.RegisterClient(session, habbo.Id, habbo.Username);
        }
        await RaiseHabboLoggedIn(habbo);
        return null;
    }

    private async Task<int> GetUserIdFromSso(string sso)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>("SELECT id FROM users WHERE auth_ticket = @sso", new { sso });
    }

    private async Task ResetSso(int userId)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("UPDATE users SET auth_ticket = NULL WHERE id = @userId", new { userId });
    }

    private async Task<bool> CanLogin(int userId)
    {
        foreach (var task in _authenticationTasks)
        {
            if (!(await task.CanLogin(userId)))
                return false;
        }
        return true;
    }

    private async Task RaiseHabboLoggedIn(Habbo habbo)
    {
        foreach (var task in _authenticationTasks)
            await task.UserLoggedIn(habbo);
    }

    private async Task OnHabboDisconnected(Habbo habbo)
    {
        foreach (var task in _authenticationTasks)
            await task.UserLoggedOut(habbo);
    }
}