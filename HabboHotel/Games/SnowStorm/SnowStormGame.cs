using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Game.SnowStorm;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm.Simulation;

namespace Plus.HabboHotel.Games.SnowStorm;

internal enum SnowStormGamePhase
{
    Loading,
    Starting,
    Running,
    Ended
}

internal enum SnowStormInput
{
    Move,
    ThrowAtPosition,
    ThrowAtHuman,
    MakeSnowball
}

/// <summary>
/// One match (Polaris SnowWarGame) on the AIR flow: GameStarted → EnterArena → StageLoad → LoadStageReady (10 s timeout)
/// → StageStarting → StageRunning → one GameStatus per 150 ms turn → StageEnding + GameEnding → rematch window.
/// Only the manager's tick calls into it.
/// </summary>
internal sealed class SnowStormGame
{
    public static readonly TimeSpan TurnDuration = TimeSpan.FromMilliseconds(SnowStormArena.SubturnsPerTurn * SnowStormArena.SubturnMilliseconds);
    public static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FullStatusInterval = TimeSpan.FromSeconds(1);
    private const int MaxCatchUpTurns = 10;
    private const string RoomType = "snowwar";

    private readonly SnowStormSettings _config;
    private readonly ISnowStormStore _store;
    private readonly ILogger _logger;
    private readonly SnowStormArena _arena;
    private readonly SnowStormServerRules _rules;
    private readonly Dictionary<int, int> _humanIds = [];
    private readonly HashSet<int> _ready = [];
    private readonly HashSet<int> _rematch = [];
    private readonly HashSet<int> _fullStatusRequests = [];
    private readonly Dictionary<int, DateTimeOffset> _lastFullStatus = [];
    private readonly List<(int UserId, SnowStormInput Kind, int[] Values, int Turn, int Subturn)> _inputs = [];
    private readonly int _totalTurns;
    private DateTimeOffset _deadline;
    private DateTimeOffset _runStart;
    private int _turnsRun;

    public SnowStormGame(int id, SnowStormLobby lobby, SnowStormSettings config, ISnowStormStore store, ILogger logger, Random random)
    {
        Id = id;
        Lobby = lobby.Snapshot();
        Arena = lobby.Arena;
        Participants = [.. lobby.Players];
        _config = config;
        _store = store;
        _logger = logger;
        _totalTurns = (int)(TimeSpan.FromSeconds(config.GameLengthSeconds) / TurnDuration);
        _arena = SnowStormArena.Create(lobby.Arena.Level, SnowStormSettings.TeamCount);
        _rules = new SnowStormServerRules(_arena);
        var spawns = _rules.ChooseSpawns(Participants.Select(player => player.TeamId).ToList(), lobby.Arena.Spawns, random);

        for (var index = 0; index < Participants.Count; index++) {
            var player = Participants[index];
            var (x, y, direction) = spawns[index];
            _humanIds[player.UserId] = _arena.AddHuman(new SnowStormPlayer(player.UserId, player.Name, player.Figure, player.Gender, player.TeamId), x, y, direction).Id;
        }
    }

    public int Id { get; }

    public SnowStormLobbySnapshot Lobby { get; }

    public SnowStormArenaDefinition Arena { get; }

    public List<SnowStormParticipant> Participants { get; }

    public SnowStormGamePhase Phase { get; private set; }

    /// <summary>When the results and rematch window closes (set once the game ends).</summary>
    public DateTimeOffset RematchEnd { get; private set; }

    public IReadOnlyCollection<int> Rematchers => _rematch;

    public int Turn => _arena.Turn;

    public int HumanId(int userId) => _humanIds.GetValueOrDefault(userId);

    public void Start(DateTimeOffset now)
    {
        var players = Participants.Select(player => player.ArenaPlayer()).ToImmutableArray();
        Broadcast(new Game2GameStartedComposer(Lobby));
        Broadcast(new Game2EnterArenaComposer(SnowStormDirectory.GameTypeId, Arena.FieldType, SnowStormSettings.TeamCount, players, _arena.Level));

        foreach (var player in players) {
            Broadcast(new Game2ArenaEnteredComposer(player));
        }

        Broadcast(new Game2StageLoadComposer(SnowStormDirectory.GameTypeId));
        Phase = SnowStormGamePhase.Loading;
        _deadline = now + LoadTimeout;
    }

    public void LoadStageReady(int userId, DateTimeOffset now)
    {
        if (Phase != SnowStormGamePhase.Loading || !_ready.Add(userId)) {
            return;
        }

        var finished = Participants.Where(player => _ready.Contains(player.UserId)).Select(player => player.UserId).ToImmutableArray();
        Broadcast(new Game2StageStillLoadingComposer(finished.Length * 100 / Participants.Count, finished));

        if (finished.Length == Participants.Count) {
            BeginStage(now);
        }
    }

    /// <summary>Advances timers and runs every turn that is due; fixed rate, so late ticks catch up (bounded).</summary>
    public void Advance(DateTimeOffset now)
    {
        if (Phase == SnowStormGamePhase.Loading && now >= _deadline) {
            BeginStage(now);
        }

        if (Phase == SnowStormGamePhase.Starting && now >= _deadline) {
            Phase = SnowStormGamePhase.Running;
            _runStart = now;
            Broadcast(new Game2StageRunningComposer(_config.GameLengthSeconds));
        }

        if (Phase != SnowStormGamePhase.Running) {
            return;
        }

        var due = Math.Min(_totalTurns, (int)((now - _runStart) / TurnDuration) + 1);

        for (var budget = MaxCatchUpTurns; _turnsRun < due && budget > 0; budget--) {
            RunTurn();
        }

        if (_turnsRun >= _totalTurns) {
            End(now);
        }
    }

    public void QueueInput(int userId, SnowStormInput kind, int[] values, int turn, int subturn)
    {
        if (Phase == SnowStormGamePhase.Running && _humanIds.ContainsKey(userId) && subturn is >= 0 and < SnowStormArena.SubturnsPerTurn) {
            _inputs.Add((userId, kind, values, turn, subturn));
        }
    }

    public void RequestFullStatus(int userId, DateTimeOffset now)
    {
        if (Phase != SnowStormGamePhase.Running || !_humanIds.ContainsKey(userId) ||
            _lastFullStatus.TryGetValue(userId, out var last) && now - last < FullStatusInterval) {
            return;
        }

        _lastFullStatus[userId] = now;
        _fullStatusRequests.Add(userId);
    }

    public void Chat(int userId, string message)
    {
        if (Participants.Any(player => player.UserId == userId)) {
            Broadcast(new Game2GameChatComposer(userId, message));
        }
    }

    public void PlayAgain(int userId)
    {
        if (Phase == SnowStormGamePhase.Ended && Participants.Any(player => player.UserId == userId) && _rematch.Add(userId)) {
            Broadcast(new Game2PlayerRematchesComposer(userId));
        }
    }

    /// <summary>
    /// Takes a player out of the match. Others see PlayerExitedGameArena and, in the arena, HumanLeftGame at the next
    /// broadcast turn; the leaver gets the block (when given) and RejoinPreviousRoom.
    /// </summary>
    public void Remove(SnowStormParticipant player, int blockSeconds, bool notifyLeaver, DateTimeOffset now)
    {
        if (!Participants.Remove(player)) {
            return;
        }

        _rematch.Remove(player.UserId);
        _ready.Remove(player.UserId);
        _fullStatusRequests.Remove(player.UserId);

        if (_humanIds.Remove(player.UserId, out var humanId) && Phase != SnowStormGamePhase.Ended) {
            // GameStatus(Turn - 1) already carried the events of Turn, so the first open slot is the turn after it.
            _arena.Schedule(_turnsRun > 0 ? _arena.Turn + 1 : 1, 0, new SnowStormHumanLeftGame(humanId));
            Broadcast(new Game2PlayerExitedGameArenaComposer(player.UserId, humanId));
        }

        if (notifyLeaver) {
            if (blockSeconds > 0) {
                player.Session.Send(new Game2UserBlockedComposer(blockSeconds));
            }

            player.Session.Send(new Game2RejoinPreviousRoomComposer(player.RoomId));
        }

        if (Phase == SnowStormGamePhase.Ended) {
            return;
        }

        if (Participants.Count == 0) {
            Phase = SnowStormGamePhase.Ended;
            RematchEnd = now;
        }
        else if (Participants.Count < Math.Min(2, _config.MinPlayers)) {
            End(now);
        }
        else if (Phase == SnowStormGamePhase.Loading && _ready.Count == Participants.Count) {
            BeginStage(now);
        }
    }

    private void BeginStage(DateTimeOffset now)
    {
        Phase = SnowStormGamePhase.Starting;
        _deadline = now + TimeSpan.FromSeconds(_config.StageCountdownSeconds);
        Broadcast(new Game2StageStartingComposer(SnowStormDirectory.GameTypeId, RoomType, _config.StageCountdownSeconds, Objects()));
    }

    private void RunTurn()
    {
        var result = _arena.RunTurn();
        _turnsRun++;

        // Inputs go to the earliest slot not yet broadcast (the next turn), never into the past and at most one turn later.
        var earliest = _arena.Turn;

        foreach (var (userId, kind, values, turn, subturn) in _inputs) {
            if (!_humanIds.TryGetValue(userId, out var humanId)) {
                continue;
            }

            var (slotTurn, slotSubturn) = turn < earliest ? (earliest, 0) : turn > earliest + 1 ? (earliest + 1, 0) : (turn, subturn);
            _ = kind switch
            {
                SnowStormInput.Move => _rules.TryScheduleMove(slotTurn, slotSubturn, humanId, values[0], values[1]),
                SnowStormInput.ThrowAtPosition => _rules.TryScheduleThrowAtPosition(slotTurn, slotSubturn, humanId, values[0], values[1], values[2]),
                SnowStormInput.ThrowAtHuman => _rules.TryScheduleThrowAtHuman(slotTurn, slotSubturn, humanId, values[0], values[1]),
                _ => _rules.TryScheduleMakeSnowball(slotTurn, slotSubturn, humanId)
            };
        }

        _inputs.Clear();
        _rules.ScheduleRefillsAndPickups();
        var status = Status(result.Turn, result.Checksum);

        if (_fullStatusRequests.Count == 0) {
            Broadcast(new Game2GameStatusComposer(status));

            return;
        }

        var full = new SnowStormFullStatusSnapshot(RemainingSeconds(), _config.GameLengthSeconds, Objects(), SnowStormSettings.TeamCount, status);
        GameClient.SendBroadcast(new Game2GameStatusComposer(status), Clients(player => !_fullStatusRequests.Contains(player.UserId)));
        GameClient.SendBroadcast(new Game2FullGameStatusComposer(full), Clients(player => _fullStatusRequests.Contains(player.UserId)));
        _fullStatusRequests.Clear();
    }

    private void End(DateTimeOffset now)
    {
        Phase = SnowStormGamePhase.Ended;
        RematchEnd = now + TimeSpan.FromSeconds(_config.RematchSeconds);
        var result = Result();
        Broadcast(new Game2StageEndingComposer(0));
        Broadcast(new Game2GameEndingComposer(_config.RematchSeconds, result));
        var scores = result.Teams.SelectMany(team => team.Players).Select(player => (player.UserId, player.Score)).ToList();

        foreach (var player in Participants) {
            player.TotalScore += scores.FirstOrDefault(score => score.UserId == player.UserId).Score;
        }

        try {
            _store.RecordScores(SnowStormStore.WeekStart(now), scores);
        }
        catch (Exception exception) {
            _logger.LogError(exception, "Unable to record the scores of SnowStorm game {GameId}", Id);
        }
    }

    private SnowStormGameResult Result()
    {
        var players = Participants.Select(player => (Player: player, Human: _arena.GetObject(HumanId(player.UserId)) as SnowStormHuman)).ToList();
        var stats = players.ToDictionary(entry => entry.Player.UserId, entry => Stats(entry.Human));
        var teams = Enumerable.Range(1, SnowStormSettings.TeamCount).Select(team => new SnowStormTeamResult(team, _arena.TeamScores[team - 1],
            players.Where(entry => entry.Player.TeamId == team).Select(entry => new SnowStormTeamPlayerResult(entry.Player.Name, entry.Player.UserId,
                entry.Player.Figure, entry.Player.Gender, stats[entry.Player.UserId].Score, stats[entry.Player.UserId])).ToImmutableArray())).ToImmutableArray();
        var best = teams.MaxBy(team => team.Score)!;
        var tie = teams.Count(team => team.Score == best.Score) > 1;

        int Top(Func<SnowStormPlayerStatsSnapshot, int> value) =>
            stats.Where(entry => value(entry.Value) > 0).OrderByDescending(entry => value(entry.Value)).Select(entry => entry.Key).FirstOrDefault();

        return new(_arena.IsDeathMatch, tie ? SnowStormGameResult.Tie : SnowStormGameResult.Win, tie ? 0 : best.TeamId, teams,
            Top(stat => stat.Kills), Top(stat => stat.SnowballHits));
    }

    private SnowStormPlayerStatsSnapshot Stats(SnowStormHuman? human)
    {
        if (human == null) {
            return new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var stats = _arena.GetStats(human.Id);

        return new(human.Score, stats.Kills, stats.Deaths, stats.SnowballHits, stats.SnowballHitsTaken, stats.SnowballsThrown,
            stats.SnowballsCreated, stats.SnowballsFromMachine, stats.FriendlyHits, stats.FriendlyKills);
    }

    private SnowStormStatusSnapshot Status(int turn, int checksum)
    {
        var events = _arena.GetScheduledEvents(turn + 1);

        return new(turn, checksum, Enumerable.Range(0, SnowStormArena.SubturnsPerTurn)
            .Select(subturn => events.Where(item => item.Subturn == subturn)
                .Select(item => new SnowStormWireEvent(item.Event.Type, [.. item.Event.Fields])).ToImmutableArray())
            .ToImmutableArray());
    }

    private ImmutableArray<SnowStormWireObject> Objects() =>
        _arena.Snapshot().Select(item => new SnowStormWireObject([.. item.Variables], [.. item.Strings ?? []])).ToImmutableArray();

    private int RemainingSeconds() =>
        Math.Max(0, _config.GameLengthSeconds - (int)(_turnsRun * TurnDuration.TotalSeconds));

    private IEnumerable<GameClient> Clients(Func<SnowStormParticipant, bool> filter) =>
        Participants.Where(filter).Select(player => player.Session).ToList();

    private void Broadcast(IServerPacket composer) => GameClient.SendBroadcast(composer, Clients(_ => true));
}
