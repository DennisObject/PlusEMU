using System.Text.RegularExpressions;
using Dapper;

namespace Plus.HabboHotel.Rooms.Instance;

public class FilterComponent
{
    private Room _instance;

    public FilterComponent(Room instance)
    {
        if (instance == null)
            return;
        _instance = instance;
    }

    public bool AddFilter(string word)
    {
        if (_instance.WordFilterList.Contains(word))
            return false;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("INSERT INTO room_filter (room_id,word) VALUES (@roomId,@word)", new { roomId = _instance.Id, word });
        _instance.WordFilterList.Add(word);
        return true;
    }

    public bool RemoveFilter(string word)
    {
        if (!_instance.WordFilterList.Contains(word))
            return false;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("DELETE FROM room_filter WHERE room_id=@roomId AND word=@word", new { roomId = _instance.Id, word });
        _instance.WordFilterList.Remove(word);
        return true;
    }

    public string CheckMessage(string message)
    {
        foreach (var filter in _instance.WordFilterList)
        {
            if (message.ToLower().Contains(filter) || message == filter)
                message = Regex.Replace(message, filter, "Bobba", RegexOptions.IgnoreCase);
            else
                continue;
        }
        return message.TrimEnd(' ');
    }

    public void Cleanup()
    {
        _instance = null;
    }
}
