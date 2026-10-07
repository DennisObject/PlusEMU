using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>A player from QuickJoin until they leave the lobby or game; looks are captured when they join.</summary>
internal sealed class SnowStormParticipant(GameClient session, int userId, string name, string figure, string gender, int roomId, int totalScore)
{
    public GameClient Session { get; } = session;

    public int UserId { get; } = userId;

    public string Name { get; } = name;

    public string Figure { get; } = figure;

    public string Gender { get; } = gender;

    /// <summary>The room the player was in when they joined (-1 for none), sent back in RejoinPreviousRoom.</summary>
    public int RoomId { get; } = roomId;

    public int TotalScore { get; set; } = totalScore;

    public int TeamId { get; set; }

    /// <summary>A game was already used for the next match (kept when a start is aborted, so it is not charged twice).</summary>
    public bool Paid { get; set; }

    public SnowStormLobbyPlayer LobbyPlayer() =>
        new(UserId, Name, Figure, Gender, TeamId, SnowStormSkill.Level(TotalScore), TotalScore, SnowStormSkill.ScoreToNextLevel(TotalScore));

    public SnowStormArenaPlayer ArenaPlayer() => new(UserId, Name, Figure, Gender, TeamId);
}
