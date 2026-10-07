namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>A player joining the arena; <see cref="Team"/> is 1..N (1 = blue, 2 = red).</summary>
public sealed record SnowStormPlayer(int UserId, string Name, string Figure, string Sex, int Team, string Mission = "");

/// <summary>
/// One game object as sent in AIR <c>GameObjectsData</c>: <see cref="Variables"/> is the full checksum vector starting
/// with type and id; humans also carry <see cref="Strings"/> (name, mission, figure, sex) written after the ints.
/// </summary>
public sealed record SnowStormObjectSnapshot(int[] Variables, string[]? Strings = null)
{
    public int Type => Variables[0];

    public int Id => Variables[1];
}

/// <summary>Result of one simulated turn: its checksum and the events applied during it.</summary>
public sealed record SnowStormTurnResult(int Turn, int Checksum, IReadOnlyList<SnowStormScheduledEvent> AppliedEvents);

/// <summary>AIR <c>Game2PlayerStatsData</c> counters for one human, tracked by the simulation.</summary>
public sealed class SnowStormPlayerStats
{
    public int Kills { get; internal set; }

    public int Deaths { get; internal set; }

    public int SnowballHits { get; internal set; }

    public int SnowballHitsTaken { get; internal set; }

    public int SnowballsThrown { get; internal set; }

    public int SnowballsCreated { get; internal set; }

    public int SnowballsFromMachine { get; internal set; }

    public int FriendlyHits { get; internal set; }

    // AIR never lets a teammate be knocked down, so this stays 0; kept for the GameEnding payload.
    public int FriendlyKills { get; internal set; }
}
