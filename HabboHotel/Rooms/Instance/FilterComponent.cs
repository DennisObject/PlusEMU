using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Rooms.Instance;

public class FilterComponent
{
    private Room _instance;
    private readonly IRoomFilterStore _store;

    internal FilterComponent(Room instance, IRoomFilterStore store)
    {
        _instance = instance;
        _store = store;
    }

    public bool AddFilter(string word)
    {
        if (_instance.WordFilterList.Contains(word)) {
            return false;
        }

        _store.Add(_instance.Id, word);
        _instance.WordFilterList.Add(word);

        return true;
    }

    public bool RemoveFilter(string word)
    {
        if (!_instance.WordFilterList.Contains(word)) {
            return false;
        }

        _store.Remove(_instance.Id, word);
        _instance.WordFilterList.Remove(word);

        return true;
    }

    public string CheckMessage(string message)
    {
        foreach (var filter in _instance.WordFilterList) {
            if (message.ToLower().Contains(filter) || message == filter) {
                message = Regex.Replace(message, filter, "Bobba", RegexOptions.IgnoreCase);
            }
            else {
                continue;
            }
        }

        return message.TrimEnd(' ');
    }

    public void Cleanup()
    {
        _instance = null;
    }
}
