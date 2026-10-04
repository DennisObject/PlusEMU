namespace Plus.HabboHotel.Users.Authentication;

/// <summary>Reads and writes the account columns of the users table for the login API.</summary>
public interface IAccountStore
{
    Task<AccountCredentials?> FindByUsername(string username);

    /// <summary>Replaces the stored password only if it still holds <paramref name="current"/>.</summary>
    Task UpgradePassword(int userId, string current, string replacement);

    Task<string?> UsernameById(int userId);

    Task<bool> UsernameExists(string username);

    Task<bool> EmailExists(string email);

    /// <summary>Creates the user and its statistics row. Null when the username is already taken.</summary>
    Task<int?> Create(NewAccount account);
}

public sealed record AccountCredentials(int Id, string Username, string? Password);

public sealed record NewAccount(string Username, string PasswordHash, string Email, string Look, string Gender, string Address);
