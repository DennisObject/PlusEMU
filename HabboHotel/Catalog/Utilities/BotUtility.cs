using Plus.HabboHotel.Rooms.AI;

namespace Plus.HabboHotel.Catalog.Utilities;

public static class BotUtility
{
    public static BotAiType GetAiFromString(string type)
    {
        switch (type) {
            case "pet":
                return BotAiType.Pet;
            case "generic":
                return BotAiType.Generic;
            case "bartender":
                return BotAiType.Bartender;
            default:
                return BotAiType.Generic;
        }
    }
}
