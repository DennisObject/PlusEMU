namespace Plus.Communication.Http;

/// <summary>Machine-readable "code" sent next to "error" in every error response.</summary>
public static class AuthErrorCode
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string RateLimited = "rate_limited";
    public const string Banned = "banned";
    public const string NameTaken = "name_taken";
    public const string EmailTaken = "email_taken";
    public const string Validation = "validation";
    public const string InvalidTicket = "invalid_ticket";
    public const string NotImplemented = "not_implemented";
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string PayloadTooLarge = "payload_too_large";
    public const string ServerError = "server_error";
}
