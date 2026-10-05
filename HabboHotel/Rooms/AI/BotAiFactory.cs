using Plus.HabboHotel.Bots;
using Plus.HabboHotel.Rooms.AI.Types;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Rooms.Chat.Pets.Commands;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;

namespace Plus.HabboHotel.Rooms.AI;

public sealed class BotAiFactory(IPetLocale petLocale, IPetCommandManager petCommands, IWordFilterManager wordFilter, IBotManager bots) : IBotAiFactory
{
    public BotAi Create(BotAiType type, int virtualId) => type switch
    {
        BotAiType.Pet => new PetBot(virtualId, petLocale, petCommands),
        BotAiType.Bartender => new BartenderBot(virtualId, bots, wordFilter),
        _ => new GenericBot(virtualId, wordFilter)
    };
}
