using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;
using Plus.HabboHotel.Users;
using Xunit;
using static Plus.Tests.SnowStorm.SnowStormTestSupport;

namespace Plus.Tests.SnowStorm;

public class SnowStormRobustnessTests
{
    private readonly TestClock _clock = new();
    private readonly Store _store = new();

    [Fact]
    public void SpammedMovesBroadcastOneTargetAndExcessInputsAreDropped()
    {
        var manager = Manager(_store, _clock, null, ("gamecenter.snowwar.arenas", "8"));
        var players = Start(manager, 2);
        players[0].Sent.Clear();

        for (var index = 1; index <= 20; index++) {
            manager.SetMoveTarget(players[0].Client, index * 3200, 20 * 3200, 0, 0);
        }

        _clock.Advance(SnowStormGame.TurnDuration);
        manager.Tick();
        var status = Read(players[0].Sent.Last(packet => packet.Header == ServerPacketHeader.Game2GameStatusComposer).Payload);
        status.ReadInt();
        status.ReadInt();
        status.ReadInt();
        var events = status.ReadInt();
        Assert.Equal(1, events);
        Assert.Equal(2, status.ReadInt());
        status.ReadInt();
        // Only the first 12 inputs were queued; the latest of those is the move that counts.
        Assert.Equal(12 * 3200, status.ReadInt());
    }

    [Fact]
    public void PlayersWithoutGamesAreDroppedVisiblyAndDatabaseErrorsAreGeneric()
    {
        var manager = Manager(_store, _clock);
        var players = Join(manager, 4);
        _store.Accounts[3] = new SnowStormAccount(0, 10, 0);
        _store.Broken.Add(4);
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();

        var left = players[0].Sent.Where(packet => packet.Header == ServerPacketHeader.Game2UserLeftGameComposer).Select(packet => Read(packet.Payload).ReadInt());
        Assert.Equal([3, 4], left);
        Assert.Equal(8, Read(players[2].Sent.Last(packet => packet.Header == ServerPacketHeader.Game2JoiningGameFailedComposer).Payload).ReadInt());
        Assert.Equal(1, Read(players[3].Sent.Last(packet => packet.Header == ServerPacketHeader.Game2JoiningGameFailedComposer).Payload).ReadInt());
        Assert.Contains(players[0].Sent, packet => packet.Header == ServerPacketHeader.Game2GameStartedComposer);
        Assert.DoesNotContain(players[2].Sent, packet => packet.Header == ServerPacketHeader.Game2GameStartedComposer);
    }

    [Fact]
    public void AnAbortedStartKeepsThePaymentAndRefundsItWhenThePlayerLeaves()
    {
        var manager = Manager(_store, _clock, null, ("gamecenter.snowwar.players.min", "3"));
        var players = Join(manager, 3);
        _store.Accounts[3] = new SnowStormAccount(0, 10, 0);
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        Assert.Equal([1, 2], _store.Consumed);
        Assert.Equal(ServerPacketHeader.Game2StopCounterComposer, players[0].Sent[^1].Header);

        // A new player fills the lobby again: only they pay at the next start.
        var dee = Player(4, "Dee");
        manager.QuickJoin(dee.Client);
        manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        Assert.Equal([1, 2, 4], _store.Consumed);
        Assert.Contains(dee.Sent, packet => packet.Header == ServerPacketHeader.Game2GameStartedComposer);
        Assert.Empty(_store.Refunds);

        var store = new Store();
        var aborted = Manager(store, _clock, null, ("gamecenter.snowwar.players.min", "3"));
        var waiting = Join(aborted, 3);
        store.Accounts[3] = new SnowStormAccount(0, 10, 0);
        _clock.Advance(TimeSpan.FromSeconds(15));
        aborted.Tick();
        aborted.LeaveLobby(waiting[0].Client);
        aborted.Tick();
        Assert.Equal([1], store.Refunds);
    }

    [Fact]
    public void AFailedStartTellsThePlayersAndRefundsTheirGames()
    {
        var tiny = new FixedArenas(SnowStormArenas.Parse("""{ "fieldType": 8, "name": "Tiny", "heightmap": ["0"], "items": [] }"""));
        var manager = Manager(_store, _clock, tiny);
        var players = Join(manager, 2);
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();

        Assert.All(players, player => Assert.Equal(ServerPacketHeader.Game2StartingGameFailedComposer, player.Sent[^1].Header));
        Assert.Equal([1, 2], _store.Refunds);

        players[0].Sent.Clear();
        manager.QuickJoin(players[0].Client);
        manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2GameCreatedComposer], players[0].Sent.Select(packet => packet.Header));
    }

    [Fact]
    public void DisconnectingFromARunningGameStillBlocksAndClosedSessionsNeverJoin()
    {
        var manager = Manager(_store, _clock);
        var players = Start(manager, 2);
        players[0].Sent.Clear();
        players[0].Habbo.Dispose();
        manager.Tick();
        Assert.Equal(180, manager.BlockSeconds(1));
        Assert.Empty(players[0].Sent);

        var closed = Player(5, "Eve");
        closed.Habbo.Dispose();
        manager.QuickJoin(closed.Client);
        manager.Tick();
        Assert.Empty(closed.Sent);
    }

    [Fact]
    public void EachLeaderboardIsServedAtMostOnceASecondPerPlayer()
    {
        var player = Player(1, "Ann");
        var directory = new SnowStormDirectory(_store, Manager(_store, _clock), new Settings(("gamecenter.snowwar.enabled", "1")), _clock,
            NullLogger<SnowStormDirectory>.Instance);
        directory.ShowLeaderboard(player.Client, SnowStormLeaderboardKind.Total, 0, 0, -1, 8, 50);
        directory.ShowLeaderboard(player.Client, SnowStormLeaderboardKind.Total, 0, 0, -1, 8, 50);
        directory.ShowLeaderboard(player.Client, SnowStormLeaderboardKind.Weekly, 0, 0, -1, 8, 50);
        _clock.Advance(TimeSpan.FromSeconds(1));
        directory.ShowLeaderboard(player.Client, SnowStormLeaderboardKind.Total, 0, 0, -1, 8, 50);
        Assert.Equal([SnowStormLeaderboardKind.Total, SnowStormLeaderboardKind.Weekly, SnowStormLeaderboardKind.Total], _store.Requests.Select(request => request.Kind));
    }

    private List<(GameClient Client, List<(uint Header, byte[] Payload)> Sent, Habbo Habbo)> Join(SnowStormManager manager, int count)
    {
        var players = Enumerable.Range(1, count).Select(id => Player(id, "P" + id)).ToList();

        foreach (var player in players) {
            manager.QuickJoin(player.Client);
        }

        manager.Tick();

        return players;
    }

    private List<(GameClient Client, List<(uint Header, byte[] Payload)> Sent, Habbo Habbo)> Start(SnowStormManager manager, int count)
    {
        var players = Join(manager, count);
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        _clock.Advance(SnowStormGame.LoadTimeout);
        manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(5));
        manager.Tick();

        return players;
    }

    private sealed class FixedArenas(SnowStormArenaDefinition arena) : ISnowStormArenas
    {
        public IReadOnlyList<SnowStormArenaDefinition> All => [arena];

        public bool TryGet(int fieldType, [NotNullWhen(true)] out SnowStormArenaDefinition? found)
        {
            found = arena.FieldType == fieldType ? arena : null;

            return found != null;
        }
    }
}
