namespace Plus.HabboHotel.Rooms.Chat.Filter;

public interface IWordFilterManager
{
    void Init();
    string CheckMessage(string message);
    bool CheckBannedWords(string message);
    bool IsFiltered(string message);
}
