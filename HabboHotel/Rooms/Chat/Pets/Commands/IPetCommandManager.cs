namespace Plus.HabboHotel.Rooms.Chat.Pets.Commands;

public interface IPetCommandManager
{
    void Init();
    int TryInvoke(string input);
}
