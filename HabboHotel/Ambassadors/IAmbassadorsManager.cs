using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Ambassadors;

public interface IAmbassadorsManager
{
    Task Warn(GameClient session, int targetId, string message);
}
