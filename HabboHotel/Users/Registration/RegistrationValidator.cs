using System.Buffers;
using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Users.Registration;

/// <summary>
/// Input rules for new accounts. Name rules mirror the in-game name change check
/// (CheckUserNameEvent): 3-15 characters from its alphabet and no staff-looking fragments.
/// </summary>
public static partial class RegistrationValidator
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 15;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxEmailLength = 254;
    public const int MaxFigureLength = 255;

    private static readonly SearchValues<char> UsernameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,_-;:?!");
    private static readonly string[] StaffFragments = ["mod", "m0d", "adm"];

    public static string? UsernameError(string username, IEnumerable<string> reservedNames, Func<string, bool> isFiltered)
    {
        if (username.Length < MinUsernameLength || username.Length > MaxUsernameLength)
        {
            return $"Your Habbo name must be {MinUsernameLength} to {MaxUsernameLength} characters long.";
        }

        if (username.AsSpan().IndexOfAnyExcept(UsernameCharacters) >= 0)
        {
            return "Your Habbo name can only use letters, numbers and . , _ - ; : ? !";
        }

        var lower = username.ToLowerInvariant();

        if (StaffFragments.Concat(reservedNames).Any(fragment => fragment.Length > 0 && lower.Contains(fragment.ToLowerInvariant())) || isFiltered(lower))
        {
            return "That Habbo name is not allowed.";
        }

        return null;
    }

    public static string? EmailError(string email)
    {
        if (email.Length > MaxEmailLength || !EmailPattern().IsMatch(email))
        {
            return "Please enter a valid email address.";
        }

        return null;
    }

    public static string? PasswordError(string password, string username)
    {
        if (password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            return $"Your password must be {MinPasswordLength} to {MaxPasswordLength} characters long.";
        }

        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
        {
            return "Your password cannot be your Habbo name.";
        }

        return null;
    }

    /// <summary>The submitted figure if it is a well-formed figure string, else the fallback.
    /// The game still checks every part against figuredata on each login.</summary>
    public static string FigureOrDefault(string? figure, string fallback) =>
        figure != null && figure.Length <= MaxFigureLength && FigurePattern().IsMatch(figure) ? figure : fallback;

    public static string Gender(string? gender) => string.Equals(gender, "F", StringComparison.OrdinalIgnoreCase) ? "F" : "M";

    // users.mail is latin1, so only plain ASCII addresses are accepted.
    [GeneratedRegex(@"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]{1,64}@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[a-z]{2}-\d{1,9}(?:-\d{1,9}){0,2}(?:\.[a-z]{2}-\d{1,9}(?:-\d{1,9}){0,2})*$")]
    private static partial Regex FigurePattern();
}
