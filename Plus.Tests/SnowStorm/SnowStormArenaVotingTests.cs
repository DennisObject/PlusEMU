using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;
using Plus.HabboHotel.Users;
using Xunit;
using static Plus.Tests.SnowStorm.SnowStormTestSupport;

namespace Plus.Tests.SnowStorm;

public class SnowStormArenaVotingTests
{
    private readonly TestClock _clock = new();
    private readonly Store _store = new();

    [Fact]
    public void VotesFollowJoinsVotesReVotesAndLeaves()
    {
        var manager = Manager(_store, _clock);
        var ann = Player(1, "Ann");
        var bo = Player(2, "Bo");
        manager.QuickJoin(ann.Client);
        manager.Tick();
        Assert.Equal("8:0 9:0 11:0 | 0", LastVotes(ann));

        manager.QuickJoin(bo.Client);
        manager.Tick();
        Assert.Equal("8:0 9:0 11:0 | 0", LastVotes(bo));

        manager.VoteArena(ann.Client, 11);
        manager.Tick();
        Assert.Equal("8:0 9:0 11:1 | 11", LastVotes(ann));
        Assert.Equal("8:0 9:0 11:1 | 11", LastVotes(bo));

        // Votes are rate limited like chat; after that a re-vote moves the vote.
        manager.VoteArena(ann.Client, 9);
        manager.Tick();
        Assert.Equal("8:0 9:0 11:1 | 11", LastVotes(ann));
        _clock.Advance(TimeSpan.FromSeconds(1));
        manager.VoteArena(ann.Client, 9);
        manager.VoteArena(bo.Client, 8);
        manager.Tick();
        Assert.Equal("8:1 9:1 11:0 | 0", LastVotes(bo));

        // Unknown arenas are ignored, and a leaving player's vote goes with them.
        bo.Sent.Clear();
        _clock.Advance(TimeSpan.FromSeconds(1));
        manager.VoteArena(bo.Client, 5);
        manager.Tick();
        Assert.Empty(bo.Sent);
        manager.LeaveLobby(bo.Client);
        manager.Tick();
        Assert.Equal("8:0 9:1 11:0 | 9", LastVotes(ann));

        // Outside a lobby a vote does nothing.
        var outsider = Player(3, "Cy");
        manager.VoteArena(outsider.Client, 8);
        manager.Tick();
        Assert.Empty(outsider.Sent);
    }

    [Fact]
    public void TheMostVotedArenaIsPlayedAndARematchKeepsItUntilThePlayersVoteAgain()
    {
        var manager = Manager(_store, _clock, null, ("gamecenter.snowwar.game.length.seconds", "10"));
        var players = new[] { Player(1, "Ann"), Player(2, "Bo"), Player(3, "Cy") };

        foreach (var player in players) {
            manager.QuickJoin(player.Client);
        }

        manager.Tick();
        manager.VoteArena(players[0].Client, 11);
        manager.VoteArena(players[1].Client, 11);
        manager.VoteArena(players[2].Client, 9);
        manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        Assert.Equal(11, FieldType(players[0], ServerPacketHeader.Game2GameStartedComposer));
        var enter = Read(players[2].Sent.Last(packet => packet.Header == ServerPacketHeader.Game2EnterArenaComposer).Payload);
        enter.ReadInt();
        Assert.Equal(11, enter.ReadInt());

        _clock.Advance(SnowStormGame.LoadTimeout);
        manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(5));
        manager.Tick();

        for (var turn = 0; turn < 70; turn++) {
            _clock.Advance(SnowStormGame.TurnDuration);
            manager.Tick();
        }

        manager.PlayAgain(players[0].Client);
        manager.PlayAgain(players[1].Client);
        manager.Tick();
        players[0].Sent.Clear();
        _clock.Advance(TimeSpan.FromSeconds(30));
        manager.Tick();
        Assert.Equal(11, FieldType(players[0], ServerPacketHeader.Game2GameCreatedComposer));
        // Votes start empty in the rematch lobby, and with no votes it keeps the arena.
        Assert.Equal("8:0 9:0 11:0 | 11", LastVotes(players[0]));
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        Assert.Equal(11, FieldType(players[0], ServerPacketHeader.Game2GameStartedComposer));
    }

    [Fact]
    public void TiesAreBrokenUniformlyAtRandom()
    {
        var arenas = Arenas();
        Assert.True(arenas.TryGet(8, out var arctic));
        int[] offered = [8, 9, 11];
        var random = new Random(1234);
        var lobby = new SnowStormLobby(1, arctic, 8);

        var none = Draw(lobby, offered, random);
        Assert.Equal([8, 9, 11], none.Keys.Order());
        Assert.All(none.Values, count => Assert.InRange(count, 900, 1100));

        lobby.Votes[1] = 8;
        lobby.Votes[2] = 11;
        var tied = Draw(lobby, offered, random);
        Assert.Equal([8, 11], tied.Keys.Order());
        Assert.All(tied.Values, count => Assert.InRange(count, 1350, 1650));
        Assert.Equal(0, lobby.Leading(offered));

        lobby.Votes[3] = 11;
        Assert.Equal([11], Draw(lobby, offered, random).Keys);
        Assert.Equal(11, lobby.Leading(offered));

        var rematch = new SnowStormLobby(2, arctic, 8) { KeepArena = true };
        Assert.Equal([8], Draw(rematch, offered, random).Keys);
        Assert.Equal(8, rematch.Leading(offered));
        rematch.Votes[1] = 9;
        Assert.Equal([9], Draw(rematch, offered, random).Keys);
    }

    private static Dictionary<int, int> Draw(SnowStormLobby lobby, int[] offered, Random random) =>
        Enumerable.Range(0, 3000).Select(_ => lobby.ChooseArena(offered, random)).GroupBy(fieldType => fieldType)
            .ToDictionary(group => group.Key, group => group.Count());

    // "fieldType:votes ... | leading" of the player's latest SnowStormArenaVotes.
    private static string LastVotes((GameClient Client, List<(uint Header, byte[] Payload)> Sent, Habbo Habbo) player)
    {
        var packet = Read(player.Sent.Last(sent => sent.Header == ServerPacketHeader.SnowStormArenaVotesComposer).Payload);
        var arenas = Enumerable.Range(0, packet.ReadInt()).Select(_ => $"{packet.ReadInt()}:{packet.ReadInt()}").ToList();

        return string.Join(' ', arenas) + " | " + packet.ReadInt();
    }

    // GameLobbyData: game id, level name, game type, then the field type.
    private static int FieldType((GameClient Client, List<(uint Header, byte[] Payload)> Sent, Habbo Habbo) player, uint header)
    {
        var packet = Read(player.Sent.Last(sent => sent.Header == header).Payload);
        packet.ReadInt();
        packet.ReadString();
        packet.ReadInt();

        return packet.ReadInt();
    }
}
