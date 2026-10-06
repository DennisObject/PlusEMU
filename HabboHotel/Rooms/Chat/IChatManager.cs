using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Emotions;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Rooms.Chat.Logs;
using Plus.HabboHotel.Rooms.Chat.Pets.Commands;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;
using Plus.HabboHotel.Rooms.Chat.Styles;

namespace Plus.HabboHotel.Rooms.Chat;

public interface IChatManager
{
    IChatEmotionsManager GetEmotions();
    IChatlogManager GetLogs();
    IWordFilterManager GetFilter();
    ICommandManager GetCommands();
    IPetCommandManager GetPetCommands();
    IPetLocale GetPetLocale();
    IChatStyleManager GetChatStyles();
    void Init();
}
