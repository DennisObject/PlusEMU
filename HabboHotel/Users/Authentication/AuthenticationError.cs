namespace Plus.HabboHotel.Users.Authentication;

public enum AuthenticationError
{
    EmptySSO,
    InvalidSSO,
    NoAccountFound,
    LoginProhibited,
    /// <summary>The connection closed or timed out before the login finished.</summary>
    SessionClosed
}