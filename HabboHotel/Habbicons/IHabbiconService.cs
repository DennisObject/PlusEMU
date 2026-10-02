using Plus.HabboHotel.Users;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Habbicons;

[Singleton]
public interface IHabbiconService
{
    HabbiconSnapshot Load(int userId);
    HabbiconChange Change(Habbo habbo, HabbiconAction action, int id);
    HabbiconChange BuyCatalog(Habbo habbo, int id, int credits, int duckets, int diamonds);
    bool Use(int userId, int id);
    void ClearUnseen(int userId, IReadOnlyList<int> ids);
}
