namespace Plus.HabboHotel.Rooms.Chat.Logs;

public interface IChatlogManager
{
    void StoreChatlog(ChatlogEntry entry);
    void FlushAndSave();
}
