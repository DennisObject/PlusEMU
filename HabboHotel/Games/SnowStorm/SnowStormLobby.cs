using System.Collections.Immutable;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>A lobby waiting for players, its start countdown or a free arena slot.</summary>
internal sealed class SnowStormLobby(int id, SnowStormArenaDefinition arena, int maximumPlayers)
{
    public int Id { get; } = id;

    /// <summary>The arena shown in the lobby; replaced by the vote result when the game starts.</summary>
    public SnowStormArenaDefinition Arena { get; set; } = arena;

    /// <summary>Arena votes by user id (field type); one per player.</summary>
    public Dictionary<int, int> Votes { get; } = [];

    /// <summary>A rematch lobby keeps the previous arena unless its players vote.</summary>
    public bool KeepArena { get; init; }

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

    /// <summary>Votes per offered arena, in offer order.</summary>
    public ImmutableArray<(int FieldType, int Votes)> Tally(IReadOnlyList<int> offered) =>
        offered.Select(fieldType => (fieldType, Votes.Values.Count(vote => vote == fieldType))).ToImmutableArray();

    /// <summary>The arena that would be played now, or 0 while the result is still a random pick among tied arenas.</summary>
    public int Leading(IReadOnlyList<int> offered)
    {
        if (KeepArena && Votes.Count == 0) {
            return Arena.FieldType;
        }

        var leaders = Leaders(offered);

        return leaders.Count == 1 ? leaders[0] : 0;
    }

    /// <summary>The most-voted arena; ties (including no votes at all) are broken uniformly at random.</summary>
    public int ChooseArena(IReadOnlyList<int> offered, Random random)
    {
        if (KeepArena && Votes.Count == 0) {
            return Arena.FieldType;
        }

        var leaders = Leaders(offered);

        return leaders.Count == 0 ? Arena.FieldType : leaders[random.Next(leaders.Count)];
    }

    private List<int> Leaders(IReadOnlyList<int> offered)
    {
        var tally = Tally(offered);

        if (tally.IsEmpty) {
            return [];
        }

        var most = tally.Max(entry => entry.Votes);

        return tally.Where(entry => entry.Votes == most).Select(entry => entry.FieldType).ToList();
    }

    public SnowStormLobbySnapshot Snapshot() =>
        new(Id, Arena.Name, SnowStormDirectory.GameTypeId, Arena.FieldType, SnowStormSettings.TeamCount, MaximumPlayers,
            Players.FirstOrDefault()?.Name ?? string.Empty, Arena.FieldType, Players.Select(player => player.LobbyPlayer()).ToImmutableArray());
}
