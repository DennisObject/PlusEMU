using Plus.HabboHotel.Rooms.Chat.Filter;

namespace Plus.Tests;

internal sealed class FakeWordFilter(params string[] words) : IWordFilterManager
{
    public void Init() { }
    public string CheckMessage(string message) => message;
    public bool CheckBannedWords(string message) => false;
    public bool IsFiltered(string message) => words.Any(message.Contains);
}
