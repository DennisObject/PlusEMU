using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
    private readonly ISsoTicketStore _ssoTickets;
    private readonly IAccessTokenStore _accessTokens;

    public AuthEndpoints(ILoginService login, IRegistrationService registration, ISsoTicketStore ssoTickets, IAccessTokenStore accessTokens)
    {
        _login = login;
        _registration = registration;
        _ssoTickets = ssoTickets;
        _accessTokens = accessTokens;
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
        auth.MapPost("/logout", Logout);
        // Starter rooms are not implemented; an empty list lets the client skip that step.
        auth.MapGet("/room-templates", () => Results.Json(new { templates = Array.Empty<object>() }));
    }

    private async Task<IResult> Login(LoginRequest body, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password))
            return Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, "Please enter both your Habbo name and password.");

        var result = await _login.Login(body.Username.Trim(), body.Password, AuthHttpServer.ClientAddress(context));
        switch (result.Status)
        {
            case LoginStatus.Success:
                return Session(result.Session!);
            case LoginStatus.Throttled:
                AuthHttpServer.SetRetryAfter(context.Response, result.RetryAfter);
                return Error(StatusCodes.Status429TooManyRequests, AuthErrorCode.RateLimited, TooManyAttempts);
            case LoginStatus.Banned:
                return Results.Json(new
                {
                    error = "This account is banned.",
                    code = AuthErrorCode.Banned,
                    banReason = result.Ban!.Reason,
                    banExpiresAt = result.Ban.ExpiresAt
                }, statusCode: StatusCodes.Status403Forbidden);
            default:
                return Error(StatusCodes.Status401Unauthorized, AuthErrorCode.InvalidCredentials, InvalidCredentials);
        }
    }

    private async Task<IResult> Register(RegisterRequest body, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password) || string.IsNullOrWhiteSpace(body.Email))
            return Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, "Choose a Habbo name, email and password.");

        var result = await _registration.Register(new(body.Username.Trim(), body.Password, body.Email.Trim(), body.Figure, body.Gender, AuthHttpServer.ClientAddress(context)));
        return result.Status switch
        {
            RegistrationStatus.Created => Session(result.Session!),
            RegistrationStatus.UsernameTaken or RegistrationStatus.EmailTaken =>
                Results.Json(new { error = result.Error, code = TakenCode(result.Status), available = false }, statusCode: StatusCodes.Status409Conflict),
            _ => Error(StatusCodes.Status400BadRequest, AuthErrorCode.Validation, result.Error)
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
        if (string.IsNullOrEmpty(body.SsoTicket) || await _ssoTickets.Exchange(body.SsoTicket) is not { } userId)
            return Error(StatusCodes.Status401Unauthorized, AuthErrorCode.InvalidTicket, "This login ticket is invalid or has expired.");

        var token = await _accessTokens.Issue(userId);
        return Results.Json(new { accessToken = token.Value, accessTokenExpiresAt = token.ExpiresAt });
    }

    /// <summary>Revokes the bearer access token and clears that user's outstanding game ticket,
    /// plus any ticket sent in the body by clients that log out without a token.</summary>
    private async Task<IResult> Logout(LogoutRequest body, HttpRequest request)
    {
        if (BearerToken(request) is { } token)
        {
            if (await _accessTokens.FindUser(token) is { } userId)
                await _ssoTickets.Revoke(userId);
            await _accessTokens.Revoke(token);
        }
        if (!string.IsNullOrEmpty(body.SsoTicket))
            await _ssoTickets.Consume(body.SsoTicket);
        return Results.Json(new { ok = true });
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && header.Length > 7 ? header[7..].Trim() : null;
    }

    /// <summary>Login and register both answer with the same session fields.</summary>
    private static IResult Session(AuthSession session) => Results.Json(new
    {
        ssoTicket = session.SsoTicket.Value,
        username = session.Username,
        accessToken = session.AccessToken.Value,
        accessTokenExpiresAt = session.AccessToken.ExpiresAt
    });

    private static IResult Availability(Availability result) => result.Available
        ? Results.Json(new { available = true })
        : Results.Json(new { available = false, error = result.Error, code = result.Reason == RegistrationStatus.Invalid ? AuthErrorCode.Validation : TakenCode(result.Reason) });

    private static string TakenCode(RegistrationStatus status) => status == RegistrationStatus.EmailTaken ? AuthErrorCode.EmailTaken : AuthErrorCode.NameTaken;

    private static IResult Error(int status, string code, string error) => Results.Json(new { error, code }, statusCode: status);

    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record RegisterRequest(string? Username, string? Password, string? Email, string? Figure, string? Gender);
    public sealed record UsernameRequest(string? Username);
    public sealed record EmailRequest(string? Email);
    public sealed record SsoTokenRequest(string? SsoTicket);
    public sealed record LogoutRequest(string? SsoTicket);
}
