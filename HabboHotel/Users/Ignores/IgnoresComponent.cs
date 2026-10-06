namespace Plus.HabboHotel.Users.Ignores;

public sealed class IgnoresComponent
{
    private readonly List<int> _ignoredUsers;
    public IReadOnlyCollection<int> IgnoredUsers => _ignoredUsers;

    public IgnoresComponent(List<int> ignoredUsers)
    {
        _ignoredUsers = ignoredUsers;
    }

    public bool TryAdd(int userId)
    {
        if (_ignoredUsers.Contains(userId))
        {
            return false;
        }

        _ignoredUsers.Add(userId);

        return true;
    }

    public bool IsIgnored(int userId) => _ignoredUsers.Contains(userId);

    public bool PublishIgnore(int userId) => TryAdd(userId);

    public bool PublishUnignore(int userId) => _ignoredUsers.Remove(userId);
}
