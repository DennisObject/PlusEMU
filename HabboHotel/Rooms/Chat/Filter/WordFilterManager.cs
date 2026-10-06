using Dapper;
using Plus.Core;
using System.Text.RegularExpressions;
using Plus.Database;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Chat.Filter;

public sealed class WordFilterManager : IWordFilterManager, IStartable
{
    private readonly IDatabase _database;
    private readonly List<WordFilter> _filteredWords;

    public WordFilterManager(IDatabase database)
    {
        _database = database;
        _filteredWords = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var filters = await connection.QueryAsync<WordFilter>("SELECT word, replacement, strict, bannable FROM wordfilter");
        _filteredWords.Clear();
        _filteredWords.AddRange(filters);
    }

    public string CheckMessage(string message)
    {
        foreach (var filter in _filteredWords.ToList()) {
            if (message.ToLower().Contains(filter.Word) && filter.IsStrict || message == filter.Word) {
                message = Regex.Replace(message, filter.Word, filter.Replacement, RegexOptions.IgnoreCase);
            }
            else if (message.ToLower().Contains(filter.Word) && !filter.IsStrict || message == filter.Word) {
                var words = message.Split(' ');
                message = "";

                foreach (var word in words.ToList()) {
                    if (word.ToLower() == filter.Word) {
                        message += $"{filter.Replacement} ";
                    }
                    else {
                        message += $"{word} ";
                    }
                }
            }
        }

        return message.TrimEnd(' ');
    }

    public bool CheckBannedWords(string message)
    {
        message = message.Replace(" ", "").Replace(".", "").Replace("_", "").ToLower();

        foreach (var filter in _filteredWords.ToList()) {
            if (!filter.IsBannable) {
                continue;
            }

            if (message.Contains(filter.Word)) {
                return true;
            }
        }

        return false;
    }

    public bool IsFiltered(string message)
    {
        foreach (var filter in _filteredWords.ToList()) {
            if (message.Contains(filter.Word)) {
                return true;
            }
        }

        return false;
    }
}
