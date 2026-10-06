namespace Plus.HabboHotel.Users.Ignores;

public interface IIgnoredUsersService
{
    Task<List<string>> GetIgnoredUsersByName(IReadOnlyCollection<int> userIds);
}
