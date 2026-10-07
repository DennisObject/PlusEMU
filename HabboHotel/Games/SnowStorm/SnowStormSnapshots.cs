using System.Collections.Immutable;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>AIR <c>GameLobbyPlayerData</c>.</summary>
public sealed record SnowStormLobbyPlayer(int UserId, string Name, string Figure, string Gender, int TeamId, int SkillLevel, int TotalScore, int ScoreToNextLevel);

/// <summary>AIR <c>GameLobbyData</c>.</summary>
public sealed record SnowStormLobbySnapshot(
    int GameId,
    string LevelName,
    int GameType,
    int FieldType,
    int NumberOfTeams,
    int MaximumPlayers,
    string OwningPlayerName,
    int LevelEntryId,
    ImmutableArray<SnowStormLobbyPlayer> Players);

/// <summary>AIR <c>Game2PlayerData</c>.</summary>
public sealed record SnowStormArenaPlayer(int ReferenceId, string UserName, string Figure, string Gender, int TeamId);

/// <summary>One AIR <c>GameObjectsData</c> record: the ints start with type and id, humans add four strings.</summary>
public sealed record SnowStormWireObject(ImmutableArray<int> Variables, ImmutableArray<string> Strings);

/// <summary>One GameStatus event: its type and the ints that follow it.</summary>
public sealed record SnowStormWireEvent(int Type, ImmutableArray<int> Fields);

/// <summary>AIR <c>GameStatusData</c>: events are the ones scheduled for the next turn, one list per subturn.</summary>
public sealed record SnowStormStatusSnapshot(int Turn, int Checksum, ImmutableArray<ImmutableArray<SnowStormWireEvent>> Subturns);

/// <summary>AIR <c>FullGameStatusData</c>.</summary>
public sealed record SnowStormFullStatusSnapshot(
    int RemainingSeconds,
    int DurationSeconds,
    ImmutableArray<SnowStormWireObject> Objects,
    int NumberOfTeams,
    SnowStormStatusSnapshot Status);

/// <summary>AIR <c>Game2PlayerStatsData</c>.</summary>
public sealed record SnowStormPlayerStatsSnapshot(
    int Score,
    int Kills,
    int Deaths,
    int SnowballHits,
    int SnowballHitsTaken,
    int SnowballsThrown,
    int SnowballsCreated,
    int SnowballsFromMachine,
    int FriendlyHits,
    int FriendlyKills);

/// <summary>AIR <c>Game2TeamPlayerData</c>.</summary>
public sealed record SnowStormTeamPlayerResult(string UserName, int UserId, string Figure, string Gender, int Score, SnowStormPlayerStatsSnapshot Stats);

/// <summary>AIR <c>Game2TeamScoreData</c>.</summary>
public sealed record SnowStormTeamResult(int TeamId, int Score, ImmutableArray<SnowStormTeamPlayerResult> Players);

/// <summary>AIR GameEnding body: <c>Game2GameResult</c>, the teams and <c>Game2SnowWarGameStats</c>.</summary>
public sealed record SnowStormGameResult(
    bool IsDeathMatch,
    int ResultType,
    int WinnerId,
    ImmutableArray<SnowStormTeamResult> Teams,
    int PlayerWithMostKills,
    int PlayerWithMostHits)
{
    // The client only distinguishes a tie (2); a decided game names the winning team.
    public const int Win = 1;
    public const int Tie = 2;
}

/// <summary>AIR leaderboard entry; group tables carry the group id, name and badge in the user fields.</summary>
public sealed record SnowStormLeaderboardEntry(int UserId, int Score, int Rank, string Name, string Figure, string Gender);

/// <summary>Week header of the weekly leaderboard messages.</summary>
public sealed record SnowStormLeaderboardWeek(int Year, int Week, int MaxOffset, int CurrentOffset, int MinutesUntilReset);

/// <summary>One leaderboard page; <see cref="Week"/> is set for weekly tables, <see cref="FavouriteGroupId"/> for group tables.</summary>
public sealed record SnowStormLeaderboardPage(
    ImmutableArray<SnowStormLeaderboardEntry> Entries,
    int TotalListSize,
    int GameTypeId,
    SnowStormLeaderboardWeek? Week = null,
    int FavouriteGroupId = 0);

/// <summary>A SnowStorm game-token pack sold outside the catalog.</summary>
public sealed record SnowStormTokenOffer(int Id, string LocalizationId, int PriceCredits, int PricePoints, int PointsType, int Games);
