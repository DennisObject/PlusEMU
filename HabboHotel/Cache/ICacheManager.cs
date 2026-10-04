using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.Cache.Type;

namespace Plus.HabboHotel.Cache;

public interface ICacheManager
{
    bool ContainsUser(int id);
    CachedUser? GenerateUser(int id);
    bool TryRemoveUser(int id, [NotNullWhen(true)] out CachedUser? cachedUser);
    bool TryGetUser(int id, [NotNullWhen(true)] out CachedUser? cachedUser);
    ICollection<CachedUser> GetUserCache();
    void Init();
}