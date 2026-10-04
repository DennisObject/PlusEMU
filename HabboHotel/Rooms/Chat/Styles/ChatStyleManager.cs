using System.Data;
using Microsoft.Extensions.Logging;
using Plus.Database;

namespace Plus.HabboHotel.Rooms.Chat.Styles;

public sealed class ChatStyleManager : IChatStyleManager
{
    private readonly ILogger<ChatStyleManager> _logger;
    private readonly IDatabase _database;

    private readonly Dictionary<int, ChatStyle> _styles;

    public ChatStyleManager(ILogger<ChatStyleManager> logger, IDatabase database)
    {
        _logger = logger;
        _database = database;
        _styles = new();
    }

    public void Init()
    {
        if (_styles.Count > 0)
            _styles.Clear();
        DataTable table = null;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT * FROM `room_chat_styles`;");
            table = dbClient.GetTable();
            if (table != null)
            {
                foreach (DataRow row in table.Rows)
                {
                    try
                    {
                        if (!_styles.ContainsKey(Convert.ToInt32(row["id"])))
                            _styles.Add(Convert.ToInt32(row["id"]), new(Convert.ToInt32(row["id"]), Convert.ToString(row["name"]), Convert.ToString(row["required_permission"]) ?? string.Empty, Convert.ToInt32(row["requires_hc"]) == 1, Convert.ToInt32(row["enabled"]) == 1));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Unable to load ChatBubble for ID [" + Convert.ToInt32(row["id"]) + "]", ex);
                    }
                }
            }
        }
        _logger.LogInformation("Loaded " + _styles.Count + " chat styles.");
    }

    public bool TryGetStyle(int id, out ChatStyle style) => _styles.TryGetValue(id, out style);
}