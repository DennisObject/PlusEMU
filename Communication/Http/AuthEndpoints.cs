using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Registration;

namespace Plus.Communication.Http;

/// <summary>
/// Routes used by the Octane login screen. Response fields (ssoTicket, accessToken,
/// accessTokenExpiresAt, available, error) are what the client reads.
/// </summary>
public class AuthEndpoints
{
    public const string TooManyAttempts = "Too many attempts. Please try again later.";
    private const string InvalidCredentials = "Invalid Habbo name or password.";

    private readonly ILoginService _login;
    private readonly IRegistrationService _registration;
    private readonly ISessionIssuer _sessions;

    public AuthEndpoints(ILoginService login, IRegistrationService registration, ISessionIssuer sessions)
    {
        _login = login;
        _registration = registration;
        _sessions = sessions;
    }

    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/health", () => Results.Json(new { ok = true }));
        routes.MapGet("/api/maintenance", () => Results.Json(new { enabled = false }));

        var auth = routes.MapGroup("/api/auth");
        auth.MapPost("/login", Login);
        auth.MapPost("/register", Register);
        auth.MapPost("/check-username", CheckUsername);
        auth.MapPost("/check-email", CheckEmail);
        auth.MapPost("/forgot-password", ForgotPassword);
        auth.MapPost("/sso-token", ExchangeSsoTicket);
        auth.MapPost("/remember", Remember);
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", Logout);
        // Starter rooms are not implemented; an empty list lets the client skip that step.
        auth.MapGet("/room-templates", () => Results.Json(new { templates = Array.Empty<object>() }));
    }

    private async Task<IResult> Login(LoginRequest body, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password))
            return Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, "Please enter both your Habbo name and password.");

        var result = await _login.Login(body.Username.Trim(), body.Password, AuthHttpServer.ClientAddress(context), body.Remember, context.RequestAborted);
        switch (result.Status)
        {
            case LoginStatus.Success:
                return Session(result.Session!);
            case LoginStatus.Throttled:
                AuthHttpServer.SetRetryAfter(context.Response, result.RetryAfter);
                return Error(StatusCodes.Status429TooManyRequests, AuthErrorCode.RateLimited, TooManyAttempts);
            case LoginStatus.Banned:
                return Banned(result.Ban!);
            default:
                return Error(StatusCodes.Status401Unauthorized, AuthErrorCode.InvalidCredentials, InvalidCredentials);
        }
    }

    private async Task<IResult> Register(RegisterRequest body, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password) || string.IsNullOrWhiteSpace(body.Email))
            return Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, "Choose a Habbo name, email and password.");

        var result = await _registration.Register(new(body.Username.Trim(), body.Password, body.Email.Trim(), body.Figure, body.Gender, AuthHttpServer.ClientAddress(context)),
            context.RequestAborted);
        return result.Status switch
        {
            // Revoked within the same instant (e.g. banned): the account exists, the session does not.
            RegistrationStatus.Created => result.Session is { } session
                ? Session(session)
                : Error(StatusCodes.Status401Unauthorized, AuthErrorCode.InvalidCredentials, "Your account was created. Please log in."),
            RegistrationStatus.UsernameTaken or RegistrationStatus.EmailTaken =>
                Results.Json(new { error = result.Error, code = TakenCode(result.Status), available = false }, statusCode: StatusCodes.Status409Conflict),
            _ => Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, result.Error)
        };
    }

    /// <summary>Remember-me login: a full session (game ticket included) and the rotated token.</summary>
    private Task<IResult> Remember(RememberRequest body, HttpContext context) => Resume(body, context, withTicket: true);

    /// <summary>Keeps a remembered client's HTTP access alive: a new access token and the rotated
    /// remember token, without a game ticket.</summary>
    private Task<IResult> Refresh(RememberRequest body, HttpContext context) => Resume(body, context, withTicket: false);

    private async Task<IResult> Resume(RememberRequest body, HttpContext context, bool withTicket)
    {
        if (string.IsNullOrEmpty(body.RememberToken))
            return Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, "Missing remember token.");

        var result = await _sessions.Resume(body.RememberToken, AuthHttpServer.ClientAddress(context), withTicket);
        return result.Status switch
        {
            ResumeStatus.Resumed when withTicket => Session(result.Session!),
            ResumeStatus.Resumed => Results.Json(new
            {
                userId = result.Session!.UserId,
                username = result.Session.Username,
                accessToken = result.Session.AccessToken.Value,
                accessTokenExpiresAt = result.Session.AccessToken.ExpiresAt.ToUnixTimeSeconds(),
                rememberToken = result.Session.RememberToken!.Value.Value,
                rememberExpiresAt = result.Session.RememberToken.Value.ExpiresAt.ToUnixTimeSeconds()
            }),
            ResumeStatus.Banned => Banned(result.Ban!),
            _ => Error(StatusCodes.Status401Unauthorized, AuthErrorCode.InvalidRememberToken, "Please log in again.")
        };
    }

    private async Task<IResult> CheckUsername(UsernameRequest body) => string.IsNullOrWhiteSpace(body.Username)
        ? Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, "Choose a Habbo name.")
        : Availability(await _registration.CheckUsername(body.Username.Trim()));

    private async Task<IResult> CheckEmail(EmailRequest body) => string.IsNullOrWhiteSpace(body.Email)
        ? Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, "Enter your email address.")
        : Availability(await _registration.CheckEmail(body.Email.Trim()));

    // No mail transport exists yet. Same answer for every address, so nothing is revealed.
    private static IResult ForgotPassword(EmailRequest body) =>
        Error(StatusCodes.Status501NotImplemented, AuthErrorCode.NotImplemented, "Password reset by email is not available yet. Please contact a staff member.");

    /// <summary>
    /// Gives a client that only holds an SSO ticket (e.g. one handed over by a CMS) an access token,
    /// once per ticket. The ticket is not used up, so the game login can still redeem it.
    /// </summary>
    private async Task<IResult> ExchangeSsoTicket(SsoTokenRequest body)
    {
        if (string.IsNullOrEmpty(body.SsoTicket) || await _sessions.ExchangeTicket(body.SsoTicket) is not { } token)
            return Error(StatusCodes.Status401Unauthorized, AuthErrorCode.InvalidTicket, "This login ticket is invalid or has expired.");

        return Results.Json(new { accessToken = token.Value, accessTokenExpiresAt = token.ExpiresAt.ToUnixTimeSeconds() });
    }

    /// <summary>Ends this device's login session: the bearer token's, the body ticket's and the
    /// body remember token's session, with their ticket, access tokens and remember family.</summary>
    private async Task<IResult> Logout(LogoutRequest body, HttpRequest request)
    {
        await _sessions.Logout(BearerToken(request), body.SsoTicket, body.RememberToken);
        return Results.Json(new { ok = true });
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && header.Length > 7 ? header[7..].Trim() : null;
    }

    /// <summary>Login, register and remember answer with the same session fields; the remember
    /// fields only appear when a remember token was issued.</summary>
    private static IResult Session(AuthSession session) => session.RememberToken is { } remember
        ? Results.Json(new
        {
            ssoTicket = session.SsoTicket.Value,
            userId = session.UserId,
            username = session.Username,
            accessToken = session.AccessToken.Value,
            accessTokenExpiresAt = session.AccessToken.ExpiresAt.ToUnixTimeSeconds(),
            rememberToken = remember.Value,
            rememberExpiresAt = remember.ExpiresAt.ToUnixTimeSeconds()
        })
        : Results.Json(new
        {
            ssoTicket = session.SsoTicket.Value,
            userId = session.UserId,
            username = session.Username,
            accessToken = session.AccessToken.Value,
            accessTokenExpiresAt = session.AccessToken.ExpiresAt.ToUnixTimeSeconds()
        });

    private static IResult Banned(LoginBan ban) => Results.Json(new
    {
        error = "This account is banned.",
        code = AuthErrorCode.Banned,
        banReason = ban.Reason,
        banExpiresAt = ban.ExpiresAt?.ToUnixTimeSeconds() ?? 0
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult Availability(Availability result) => result.Available
        ? Results.Json(new { available = true })
        : Results.Json(new { available = false, error = result.Error, code = result.Reason == RegistrationStatus.Invalid ? AuthErrorCode.Validation : TakenCode(result.Reason) });

    private static string TakenCode(RegistrationStatus status) => status == RegistrationStatus.EmailTaken ? AuthErrorCode.EmailTaken : AuthErrorCode.NameTaken;

    private static IResult Error(int status, string code, string error) => Results.Json(new { error, code }, statusCode: status);

    public sealed record LoginRequest(string? Username, string? Password, bool Remember = false);
    public sealed record RegisterRequest(string? Username, string? Password, string? Email, string? Figure, string? Gender);
    public sealed record UsernameRequest(string? Username);
    public sealed record EmailRequest(string? Email);
    public sealed record SsoTokenRequest(string? SsoTicket);
    public sealed record LogoutRequest(string? SsoTicket, string? RememberToken);
    public sealed record RememberRequest(string? RememberToken);
}
