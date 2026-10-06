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
    private readonly IAccountSessionGate _sessionGate;

    public Authenticator(IEnumerable<IAuthenticationTask> authenticationTasks, IGameClientManager gameClientManager, IUserDataFactory userDataFactory, ISsoTicketStore ssoTickets,
        IAccountSessionGate sessionGate)
    {
        _authenticationTasks = authenticationTasks;
        _gameClientManager = gameClientManager;
        _userDataFactory = userDataFactory;
        _ssoTickets = ssoTickets;
        _sessionGate = sessionGate;
    }

    public async Task<AuthenticationError?> AuthenticateUsingSSO(GameClient session, string sso)
    {
        // The packet manager abandons slow logins and closes the socket; the login must stop with it.
        try {
            return await AuthenticateUsingSSO(session, sso, session.Closed);
        }
        catch (OperationCanceledException) when (session.Closed.IsCancellationRequested) {
            return AuthenticationError.SessionClosed;
        }
    }

    private async Task<AuthenticationError?> AuthenticateUsingSSO(GameClient session, string sso, CancellationToken cancellationToken)
    {
        var started = _sessionGate.Begin();
        sso = sso.Trim();

        if (string.IsNullOrEmpty(sso)) {
            return AuthenticationError.EmptySSO;
        }

        if (!Debugger.IsAttached && sso.Length < 15) {
            return AuthenticationError.InvalidSSO;
        }

        // Single use even with a debugger attached: the ticket is cleared as it is read.
        if (await _ssoTickets.Consume(sso) is not { } userId) {
            return AuthenticationError.NoAccountFound;
        }

        Habbo? habbo;

        // Staff writes to this account wait until the session is registered, so they never land under a stale load.
        using (await _sessionGate.EnterAsync(userId, cancellationToken)) {
            // A password reset after this ticket was resolved revokes the login.
            if (_sessionGate.IsRevoked(userId, started)) {
                return AuthenticationError.LoginProhibited;
            }

            var canLogin = await CanLogin(userId);

            if (!canLogin) {
                return AuthenticationError.LoginProhibited;
            }

            habbo = await _userDataFactory.Create(userId, cancellationToken);

            if (habbo == null) {
                return AuthenticationError.NoAccountFound;
            }

            var loaded = habbo;
            loaded.Disconnected += async (_, _) => await OnHabboDisconnected(loaded);

            // TODO @80O: Remove after splitting up
            loaded.Init(session);

            // A connection that closed while this login waited must never become a registered session.
            if (!session.TryAttach(loaded, () => _gameClientManager.RegisterClient(session, loaded.Id, loaded.Username))) {
                return AuthenticationError.SessionClosed;
            }
        }

        await RaiseHabboLoggedIn(habbo);

        return null;
    }

    private async Task<bool> CanLogin(int userId)
    {
        foreach (var task in _authenticationTasks) {
            if (!(await task.CanLogin(userId))) {
                return false;
            }
        }

        return true;
    }

    private async Task RaiseHabboLoggedIn(Habbo habbo)
    {
        foreach (var task in _authenticationTasks) {
            await task.UserLoggedIn(habbo);
        }
    }

    private async Task OnHabboDisconnected(Habbo habbo)
    {
        foreach (var task in _authenticationTasks) {
            await task.UserLoggedOut(habbo);
        }
    }
}
