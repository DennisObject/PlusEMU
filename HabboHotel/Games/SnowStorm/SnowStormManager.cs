using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Game.SnowStorm;
using Plus.Core;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>
/// Polaris SnowWarManager on the AIR lobby messages: quick join into the first open lobby (two balanced teams, at most
/// 8 players), start countdown once the minimum is reached, an arena queue when every game slot is busy, free daily
/// games and bought games, the leave block, and one fixed-rate ticker for every game.
/// </summary>
public sealed class SnowStormManager(
    ISnowStormStore store,
    ISnowStormArenas arenas,
    ISettingsManager settings,
    IWordFilterManager wordFilter,
    TimeProvider clock,
    ILogger<SnowStormManager> logger) : ISnowStormManager, IStartable, IDisposable
{
    // AIR JoiningGameFailed reasons: 6 = already in a game, 8 = no free games left, anything else = generic.
    internal const int JoinFailedGeneric = 1;
    internal const int JoinFailedActiveInstance = 6;
    internal const int JoinFailedNoGamesLeft = 8;
    private const int MaxChatLength = 100;
    private static readonly TimeSpan ChatInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly ConcurrentQueue<Action<DateTimeOffset>> _commands = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _blocks = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _lastChat = new();
    private readonly List<SnowStormLobby> _lobbies = [];
    private readonly List<SnowStormGame> _games = [];
    private readonly Dictionary<int, SnowStormLobby> _lobbyOf = [];
    private readonly Dictionary<int, SnowStormGame> _gameOf = [];
    private readonly Dictionary<int, Habbo> _tracked = [];
    private readonly object _trackSync = new();
    private readonly Random _random = new();
    private ITimer? _timer;
    private int _ticking;
    private int _nextId;
    private int _nextArena;

    public int StartOrder => 80;

    public Task Start()
    {
        _timer = clock.CreateTimer(_ => Tick(), null, TickInterval, TickInterval);

        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    public void QuickJoin(GameClient session)
    {
        var habbo = session.GetHabbo();
        var config = SnowStormSettings.Read(settings);

        if (!config.Enabled || arenas.All.Count == 0) {
            session.Send(new Game2JoiningGameFailedComposer(JoinFailedGeneric));

            return;
        }

        var blocked = BlockSeconds(habbo.Id);

        if (blocked > 0) {
            session.Send(new Game2UserBlockedComposer(blocked));

            return;
        }

        int totalScore;

        try {
            if (store.GetAccount(habbo.Id, Today()).GamesLeft(config.FreeGamesPerDay) == 0) {
                session.Send(new Game2JoiningGameFailedComposer(JoinFailedNoGamesLeft));

                return;
            }

            totalScore = store.GetTotalScores([habbo.Id]).GetValueOrDefault(habbo.Id);
        }
        catch (Exception exception) {
            logger.LogError(exception, "Unable to load the SnowStorm account of {UserId}", habbo.Id);
            session.Send(new Game2JoiningGameFailedComposer(JoinFailedGeneric));

            return;
        }

        var player = new SnowStormParticipant(session, habbo.Id, habbo.Username, habbo.Look, habbo.Gender.ToUpperInvariant(),
            habbo.CurrentRoom is { } room ? (int)room.Id : -1, totalScore);
        Track(habbo);
        _commands.Enqueue(now => Join(player, config, now));
    }

    public void LeaveLobby(GameClient session) => Enqueue(session, (userId, now) => Leave(userId, notify: true, now));

    public void ExitGame(GameClient session) => Enqueue(session, (userId, now) => Leave(userId, notify: true, now));

    public void LoadStageReady(GameClient session) => Enqueue(session, (userId, now) => GameOf(userId)?.LoadStageReady(userId, now));

    public void PlayAgain(GameClient session) => Enqueue(session, (userId, _) => GameOf(userId)?.PlayAgain(userId));

    public void Chat(GameClient session, string message)
    {
        var habbo = session.GetHabbo();
        var now = clock.GetUtcNow();
        message = message.Trim();

        if (message.Length == 0 || habbo.TimeMuted > 0 ||
            _lastChat.TryGetValue(habbo.Id, out var last) && now - last < ChatInterval) {
            return;
        }

        _lastChat[habbo.Id] = now;
        var filtered = wordFilter.CheckMessage(message.Length > MaxChatLength ? message[..MaxChatLength] : message);
        Enqueue(session, (userId, _) => GameOf(userId)?.Chat(userId, filtered));
    }

    public void SetMoveTarget(GameClient session, int x, int y, int turn, int subturn) =>
        Input(session, SnowStormInput.Move, [x, y], turn, subturn);

    public void ThrowAtPosition(GameClient session, int x, int y, int trajectory, int turn, int subturn) =>
        Input(session, SnowStormInput.ThrowAtPosition, [x, y, trajectory], turn, subturn);

    public void ThrowAtHuman(GameClient session, int targetHumanId, int trajectory, int turn, int subturn) =>
        Input(session, SnowStormInput.ThrowAtHuman, [targetHumanId, trajectory], turn, subturn);

    public void MakeSnowball(GameClient session, int turn, int subturn) =>
        Input(session, SnowStormInput.MakeSnowball, [], turn, subturn);

    public void RequestFullStatus(GameClient session, int reason) =>
        Enqueue(session, (userId, now) => GameOf(userId)?.RequestFullStatus(userId, now));

    public int BlockSeconds(int userId)
    {
        if (!_blocks.TryGetValue(userId, out var until)) {
            return 0;
        }

        var left = (int)Math.Ceiling((until - clock.GetUtcNow()).TotalSeconds);

        if (left <= 0) {
            _blocks.TryRemove(userId, out _);
        }

        return Math.Max(0, left);
    }

    public void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) {
            return;
        }

        try {
            var now = clock.GetUtcNow();

            while (_commands.TryDequeue(out var command)) {
                Run(() => command(now));
            }

            if (_games.Count == 0 && _lobbies.Count == 0) {
                return;
            }

            var config = SnowStormSettings.Read(settings);

            foreach (var game in _games.ToList()) {
                Run(() => Advance(game, config, now));
            }

            Run(() => AdvanceLobbies(config, now));
        }
        finally {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private void Run(Action action)
    {
        try {
            action();
        }
        catch (Exception exception) {
            logger.LogError(exception, "SnowStorm tick failed");
        }
    }

    private void Enqueue(GameClient session, Action<int, DateTimeOffset> command)
    {
        var userId = session.GetHabbo().Id;
        _commands.Enqueue(now => command(userId, now));
    }

    private void Input(GameClient session, SnowStormInput kind, int[] values, int turn, int subturn) =>
        Enqueue(session, (userId, _) => GameOf(userId)?.QueueInput(userId, kind, values, turn, subturn));

    private SnowStormGame? GameOf(int userId) => _gameOf.GetValueOrDefault(userId);

    private void Join(SnowStormParticipant player, SnowStormSettings config, DateTimeOffset now)
    {
        if (_gameOf.TryGetValue(player.UserId, out var current)) {
            if (current.Phase != SnowStormGamePhase.Ended) {
                player.Session.Send(new Game2JoiningGameFailedComposer(JoinFailedActiveInstance));

                return;
            }

            // "Play again" from the results screen joins a fresh lobby.
            current.Remove(current.Participants.First(member => member.UserId == player.UserId), 0, false, now);
            _gameOf.Remove(player.UserId);
        }

        if (_lobbyOf.TryGetValue(player.UserId, out var joined)) {
            player.Session.Send(new Game2GameLongDataComposer(joined.Snapshot()));
            SendCountdown(joined, player, now);

            return;
        }

        var lobby = _lobbies.FirstOrDefault(candidate => !candidate.IsFull);
        var created = lobby == null;

        if (lobby == null) {
            if (!arenas.TryGet(NextArena(config), out var arena)) {
                player.Session.Send(new Game2JoiningGameFailedComposer(JoinFailedGeneric));

                return;
            }

            lobby = new SnowStormLobby(++_nextId, arena, config.MaxPlayers);
            _lobbies.Add(lobby);
        }

        player.TeamId = lobby.NextTeam();
        AddToLobby(lobby, player, created);
        SendCountdown(lobby, player, now);
    }

    private void AddToLobby(SnowStormLobby lobby, SnowStormParticipant player, bool created)
    {
        foreach (var member in lobby.Players) {
            member.Session.Send(new Game2UserJoinedGameComposer(player.LobbyPlayer(), false));
        }

        lobby.Players.Add(player);
        _lobbyOf[player.UserId] = lobby;
        player.Session.Send(created ? new Game2GameCreatedComposer(lobby.Snapshot()) : new Game2GameLongDataComposer(lobby.Snapshot()));
    }

    private static void SendCountdown(SnowStormLobby lobby, SnowStormParticipant player, DateTimeOffset now)
    {
        if (lobby.QueuedAt != null) {
            player.Session.Send(new Game2InArenaQueueComposer(lobby.QueuePosition));
        }
        else if (lobby.CountdownEnd is { } end) {
            player.Session.Send(new Game2StartCounterComposer(Math.Max(0, (int)Math.Ceiling((end - now).TotalSeconds))));
        }
    }

    private int NextArena(SnowStormSettings config)
    {
        var available = config.Arenas.Where(fieldType => arenas.TryGet(fieldType, out _)).ToList();

        if (available.Count == 0) {
            return arenas.All.Count > 0 ? arenas.All[0].FieldType : -1;
        }

        return available[_nextArena++ % available.Count];
    }

    private void Leave(int userId, bool notify, DateTimeOffset now)
    {
        if (_lobbyOf.Remove(userId, out var lobby)) {
            lobby.Players.RemoveAll(player => player.UserId == userId);

            foreach (var member in lobby.Players) {
                member.Session.Send(new Game2UserLeftGameComposer(userId));
            }

            if (lobby.Players.Count == 0) {
                _lobbies.Remove(lobby);
            }
        }

        if (_gameOf.Remove(userId, out var game)) {
            var player = game.Participants.FirstOrDefault(member => member.UserId == userId);

            if (player != null) {
                var block = notify && game.Phase == SnowStormGamePhase.Running ? SnowStormSettings.Read(settings).LeaveBlockSeconds : 0;

                if (block > 0) {
                    _blocks[userId] = now + TimeSpan.FromSeconds(block);
                }

                game.Remove(player, block, notify, now);
            }
        }

        Untrack(userId);
    }

    private void Advance(SnowStormGame game, SnowStormSettings config, DateTimeOffset now)
    {
        game.Advance(now);

        if (game.Phase != SnowStormGamePhase.Ended || now < game.RematchEnd) {
            return;
        }

        _games.Remove(game);
        var rematchers = game.Participants.Where(player => game.Rematchers.Contains(player.UserId)).ToList();

        foreach (var player in game.Participants) {
            _gameOf.Remove(player.UserId);
            player.Session.Send(new Game2RejoinPreviousRoomComposer(player.RoomId));

            if (!rematchers.Contains(player)) {
                Untrack(player.UserId);
            }
        }

        if (rematchers.Count == 0) {
            return;
        }

        // Rematch: the same players (and teams) in a new lobby on the same arena.
        var lobby = new SnowStormLobby(++_nextId, game.Arena, config.MaxPlayers);
        _lobbies.Add(lobby);

        foreach (var player in rematchers) {
            AddToLobby(lobby, player, true);
        }
    }

    private void AdvanceLobbies(SnowStormSettings config, DateTimeOffset now)
    {
        foreach (var lobby in _lobbies.ToList()) {
            if (lobby.Players.Count < config.MinPlayers) {
                if (lobby.CountdownEnd != null || lobby.QueuedAt != null) {
                    lobby.CountdownEnd = null;
                    lobby.QueuedAt = null;
                    Broadcast(lobby, new Game2StopCounterComposer());
                }

                continue;
            }

            if (lobby.CountdownEnd == null && lobby.QueuedAt == null) {
                lobby.CountdownEnd = now + TimeSpan.FromSeconds(config.LobbyCountdownSeconds);
                Broadcast(lobby, new Game2StartCounterComposer(config.LobbyCountdownSeconds));
            }

            if (lobby.CountdownEnd is { } end && now >= end) {
                lobby.CountdownEnd = null;
                lobby.QueuedAt = now;
            }
        }

        var queued = _lobbies.Where(lobby => lobby.QueuedAt != null).OrderBy(lobby => lobby.QueuedAt).ToList();

        foreach (var lobby in queued) {
            if (_games.Count(game => game.Phase != SnowStormGamePhase.Ended) < config.MaxConcurrentGames) {
                StartGame(lobby, config, now);

                continue;
            }

            var position = queued.Where(candidate => _lobbies.Contains(candidate)).ToList().IndexOf(lobby) + 1;

            if (position != lobby.QueuePosition) {
                lobby.QueuePosition = position;
                Broadcast(lobby, new Game2InArenaQueueComposer(position));
            }
        }
    }

    private void StartGame(SnowStormLobby lobby, SnowStormSettings config, DateTimeOffset now)
    {
        _lobbies.Remove(lobby);

        foreach (var player in lobby.Players.ToList()) {
            _lobbyOf.Remove(player.UserId);

            try {
                player.Paid = player.Paid || store.TryConsumeGame(player.UserId, DateOnly.FromDateTime(now.UtcDateTime), config.FreeGamesPerDay);
            }
            catch (Exception exception) {
                logger.LogError(exception, "Unable to use a SnowStorm game of {UserId}", player.UserId);
            }

            if (!player.Paid) {
                lobby.Players.Remove(player);
                player.Session.Send(new Game2JoiningGameFailedComposer(JoinFailedNoGamesLeft));
                Untrack(player.UserId);
            }
        }

        if (lobby.Players.Count < config.MinPlayers) {
            // Not enough paid-up players left: the rest keep waiting, already paid for their next start.
            lobby.QueuedAt = null;
            lobby.QueuePosition = 0;
            _lobbies.Add(lobby);

            foreach (var player in lobby.Players) {
                _lobbyOf[player.UserId] = lobby;
                player.Session.Send(new Game2StopCounterComposer());
            }

            return;
        }

        lobby.BalanceTeams();
        var game = new SnowStormGame(++_nextId, lobby, config, store, logger, _random);
        _games.Add(game);

        foreach (var player in game.Participants) {
            player.Paid = false;
            _gameOf[player.UserId] = game;
        }

        game.Start(now);
    }

    private static void Broadcast(SnowStormLobby lobby, Communication.Packets.IServerPacket composer) =>
        GameClient.SendBroadcast(composer, lobby.Players.Select(player => player.Session).ToList());

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private void Track(Habbo habbo)
    {
        lock (_trackSync) {
            if (_tracked.TryGetValue(habbo.Id, out var previous)) {
                if (ReferenceEquals(previous, habbo)) {
                    return;
                }

                previous.Disconnected -= Disconnected;
                previous.Disposed -= Disconnected;
            }

            _tracked[habbo.Id] = habbo;
            habbo.Disconnected += Disconnected;
            habbo.Disposed += Disconnected;
        }
    }

    private void Untrack(int userId)
    {
        lock (_trackSync) {
            if (_tracked.Remove(userId, out var habbo)) {
                habbo.Disconnected -= Disconnected;
                habbo.Disposed -= Disconnected;
            }
        }
    }

    // Raised under the wallet lock: only queue the leave, the tick does the rest.
    private void Disconnected(object? sender, EventArgs args)
    {
        if (sender is Habbo habbo) {
            _commands.Enqueue(now => Leave(habbo.Id, notify: false, now));
        }
    }
}
