using System.Collections.Immutable;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>A lobby waiting for players, its start countdown or a free arena slot.</summary>
internal sealed class SnowStormLobby(int id, SnowStormArenaDefinition arena, int maximumPlayers)
{
    public int Id { get; } = id;

    public SnowStormArenaDefinition Arena { get; } = arena;

    public List<SnowStormParticipant> Players { get; } = [];

    public int MaximumPlayers { get; } = maximumPlayers;

    public bool IsFull => Players.Count >= MaximumPlayers;

    /// <summary>When the running start countdown ends; null while waiting for players.</summary>
    public DateTimeOffset? CountdownEnd { get; set; }

    /// <summary>Set once the countdown is over but every arena slot is taken; orders the arena queue.</summary>
    public DateTimeOffset? QueuedAt { get; set; }

    public int QueuePosition { get; set; }

    /// <summary>The players' games are being paid off the ticker; the lobby takes no joins until the start finishes.</summary>
    public bool Starting { get; set; }

    /// <summary>The smaller team, team 1 on a tie, so teams never differ by more than one.</summary>
    public int NextTeam() =>
        Players.Count(player => player.TeamId == 1) <= Players.Count(player => player.TeamId == 2) ? 1 : 2;

    /// <summary>Rematch lobbies keep teams, so they can drift apart; move the latest joiners until sizes differ by at most one.</summary>
    public void BalanceTeams()
    {
        while (true) {
            var blue = Players.Count(player => player.TeamId == 1);
            var red = Players.Count(player => player.TeamId == 2);

            if (Math.Abs(blue - red) <= 1) {
                return;
            }

            var from = blue > red ? 1 : 2;
            Players.Last(player => player.TeamId == from).TeamId = 3 - from;
        }
    }

    public SnowStormLobbySnapshot Snapshot() =>
        new(Id, Arena.Name, SnowStormDirectory.GameTypeId, Arena.FieldType, SnowStormSettings.TeamCount, MaximumPlayers,
            Players.FirstOrDefault()?.Name ?? string.Empty, Arena.FieldType, Players.Select(player => player.LobbyPlayer()).ToImmutableArray());
}
