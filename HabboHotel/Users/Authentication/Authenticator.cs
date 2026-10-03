using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.UserData;
using System.Diagnostics;

namespace Plus.HabboHotel.Users.Authentication;

internal class Authenticator : IAuthenticator
{
    private readonly IEnumerable<IAuthenticationTask> _authenticationTasks;
    private readonly IGameClientManager _gameClientManager;
    private readonly IUserDataFactory _userDataFactory;
    private readonly ISsoTicketStore _ssoTickets;

    public Authenticator(IEnumerable<IAuthenticationTask> authenticationTasks, IGameClientManager gameClientManager, IUserDataFactory userDataFactory, ISsoTicketStore ssoTickets)
    {
        _authenticationTasks = authenticationTasks;
        _gameClientManager = gameClientManager;
        _userDataFactory = userDataFactory;
        _ssoTickets = ssoTickets;
    }

    public async Task<AuthenticationError?> AuthenticateUsingSSO(GameClient session, string sso)
    {
        sso = sso.Trim();
        if (string.IsNullOrEmpty(sso))
            return AuthenticationError.EmptySSO;

        if (!Debugger.IsAttached && sso.Length < 15)
            return AuthenticationError.InvalidSSO;

        // Single use even with a debugger attached: the ticket is cleared as it is read.
        if (await _ssoTickets.Consume(sso) is not { } userId)
            return AuthenticationError.NoAccountFound;

        var canLogin = await CanLogin(userId);
        if (!canLogin)
            return AuthenticationError.LoginProhibited;

        var habbo = await _userDataFactory.Create(userId);
        if (habbo == null)
            return AuthenticationError.NoAccountFound;

        habbo.Disconnected += async (_, _) => await OnHabboDisconnected(habbo);

        session.SetHabbo(habbo);

        // TODO @80O: Remove after splitting up
        habbo.Init(session);
        _gameClientManager.RegisterClient(session, habbo.Id, habbo.Username);
        await RaiseHabboLoggedIn(habbo);
        return null;
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