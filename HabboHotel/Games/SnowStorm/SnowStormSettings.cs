using Plus.Core.Settings;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>
/// SnowStorm server settings under the Polaris key names. Absent or invalid rows fall back to the defaults, and the
/// game stays off unless <c>gamecenter.snowwar.enabled</c> is explicitly 1.
/// </summary>
public sealed record SnowStormSettings(
    bool Enabled,
    int MinPlayers,
    int MaxPlayers,
    int MaxConcurrentGames,
    int LobbyCountdownSeconds,
    int GameLengthSeconds,
    int StageCountdownSeconds,
    int RematchSeconds,
    int FreeGamesPerDay,
    int LeaveBlockSeconds,
    IReadOnlyList<int> Arenas)
{
    // AIR lobbies show two teams of four.
    public const int TeamCount = 2;
    public const int MaxLobbyPlayers = 8;
    public static readonly IReadOnlyList<int> DefaultArenas = [8, 9, 11];

    public static SnowStormSettings Default { get; } = new(true, 2, MaxLobbyPlayers, 1, 15, 180, 5, 30, 10, 180, DefaultArenas);

    public static SnowStormSettings Read(ISettingsManager settings)
    {
        int Get(string key, int fallback, int min, int max) =>
            int.TryParse(settings.GetOptionalValue(key), out var value) ? Math.Clamp(value, min, max) : fallback;

        var arenas = (settings.GetOptionalValue("gamecenter.snowwar.arenas") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => int.TryParse(id, out var value) ? value : -1).Where(id => id >= 0).Distinct().ToArray();

        return new(
            settings.GetOptionalValue("gamecenter.snowwar.enabled") == "1",
            Get("gamecenter.snowwar.players.min", Default.MinPlayers, 1, MaxLobbyPlayers),
            Get("gamecenter.snowwar.queue.match.max", Default.MaxPlayers, 2, MaxLobbyPlayers),
            Get("gamecenter.snowwar.games.max.concurrent", Default.MaxConcurrentGames, 1, 100),
            Get("gamecenter.snowwar.game.start.time", Default.LobbyCountdownSeconds, 1, 300),
            Get("gamecenter.snowwar.game.length.seconds", Default.GameLengthSeconds, 10, 3600),
            Get("gamecenter.snowwar.preparing.seconds", Default.StageCountdownSeconds, 1, 60),
            Get("gamecenter.snowwar.restart.seconds", Default.RematchSeconds, 1, 300),
            // Negative means unlimited free games, which AIR shows as -1.
            Get("gamecenter.games.free.daily", Default.FreeGamesPerDay, -1, 1000),
            Get("gamecenter.game.leave.block.seconds", Default.LeaveBlockSeconds, 0, 86400),
            arenas.Length > 0 ? arenas : DefaultArenas);
    }
}
