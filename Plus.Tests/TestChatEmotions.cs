using Plus.HabboHotel.Rooms.Chat.Emotions;

namespace Plus.Tests;

internal sealed class TestChatEmotions(Func<string, int>? resolve = null) : IChatEmotionsManager
{
    public static IChatEmotionsManager Unused { get; } = new TestChatEmotions(_ =>
        throw new InvalidOperationException("Unexpected room-user emotion lookup."));

    public List<string> Messages { get; } = [];

    public int GetEmotionsForText(string text)
    {
        Messages.Add(text);

        return resolve?.Invoke(text) ?? 0;
    }
}
