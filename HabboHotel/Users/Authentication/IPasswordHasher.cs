using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Users.Authentication;

[Singleton]
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks a password against a stored users.password value. Rows that still hold a
    /// plaintext password or weaker hash parameters return SuccessRehashNeeded on a match.
    /// </summary>
    PasswordVerificationResult Verify(string password, string stored);
}

public enum PasswordVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded
}
