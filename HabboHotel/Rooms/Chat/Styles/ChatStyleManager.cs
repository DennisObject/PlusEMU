using Dapper;
using Plus.Core;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Plus.Database;

namespace Plus.HabboHotel.Rooms.Chat.Styles;

public sealed class ChatStyleManager : IChatStyleManager, IStartable
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

    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var styles = await connection.QueryAsync<ChatStyle>("SELECT id, name, COALESCE(required_permission, '') AS RequiredPermission, requires_hc AS RequiresHc, enabled FROM room_chat_styles");
        _styles.Clear();
        foreach (var style in styles)
            _styles.TryAdd(style.Id, style);
        _logger.LogInformation("Loaded {Count} chat styles.", _styles.Count);
    }

    public IReadOnlyList<int> GetAllowedStyleIds(Plus.HabboHotel.Permissions.UserAccess access) =>
        _styles.Values.Where(style => style.CanUse(access)).Select(style => style.Id).Order().ToArray();

    public bool TryGetStyle(int id, [NotNullWhen(true)] out ChatStyle? style) => _styles.TryGetValue(id, out style);
}