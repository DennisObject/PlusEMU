namespace Plus.HabboHotel.Rooms.AI;

public interface IBotAiFactory
{
    BotAi Create(BotAiType type, int virtualId);
}
