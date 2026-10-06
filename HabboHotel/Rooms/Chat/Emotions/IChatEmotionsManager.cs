namespace Plus.HabboHotel.Rooms.Chat.Emotions;

public interface IChatEmotionsManager
{
    /// <summary>
    /// Searches the provided text for any emotions that need to be applied and returns the packet number.
    /// </summary>
    /// <param name="text">The text to search through</param>
    /// <returns></returns>
    int GetEmotionsForText(string text);
}
