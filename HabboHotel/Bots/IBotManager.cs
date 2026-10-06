using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Responses;

namespace Plus.HabboHotel.Bots;

public interface IBotManager
{
    Task Init();
    BotResponse? GetResponse(BotAiType type, string message);
}
