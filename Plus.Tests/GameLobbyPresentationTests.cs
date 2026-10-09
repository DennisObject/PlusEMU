using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Game.Lobby;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Game;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class GameLobbyPresentationTests
{
    [Fact]
    public async Task HandlersDecodeAndDelegate()
    {
        var service = new RecordingService();
        await new GetGameListEvent(service).Parse(null!, HabbiconTestSupport.Incoming());
        await new JoinQueueEvent(service).Parse(null!, HabbiconTestSupport.Incoming(9));
        Assert.True(service.ListRequested);
        Assert.Equal(9, service.GameId);
    }

    [Fact]
    public void MissingGameDoesNotSendAndKnownGameKeepsQueueThenLoadOrder()
    {
        var games = new Games();
        var service = new GameLobbyService(games);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        service.JoinQueue(client, 404);
        Assert.Empty(sent);
        games.GameData.Add(Game());
        service.JoinQueue(client, 9);
        Assert.Equal(new[] { ServerPacketHeader.JoinQueueComposer, ServerPacketHeader.LoadGameComposer }, sent.Select(packet => packet.Header));
        Assert.Equal(9, new FlashIncomingPacket { Buffer = sent[0].Payload }.ReadInt());
        var packet = new FlashIncomingPacket { Buffer = sent[1].Payload };
        Assert.Equal(9, packet.ReadInt());
        Assert.Equal("1365260055982", packet.ReadString());
        Assert.Equal("path/game.swf", packet.ReadString());
        Assert.Equal("best", packet.ReadString());
        Assert.Equal("showAll", packet.ReadString());
        Assert.Equal(new[] { 60, 10, 8, 6 }, Enumerable.Range(0, 4).Select(_ => packet.ReadInt()));
        Assert.Equal("assetUrl", packet.ReadString());
        Assert.Equal("path/assets", packet.ReadString());
        Assert.Equal("habboHost", packet.ReadString());
        Assert.Equal("http://fuseus-private-httpd-fe-1", packet.ReadString());
        Assert.Equal("accessToken", packet.ReadString());
        Assert.Matches("^HABBOON-Fastfood-[A-Za-z0-9]{32}-7$", packet.ReadString());
        Assert.Equal("gameServerHost", packet.ReadString());
        Assert.Equal("host", packet.ReadString());
        Assert.Equal("gameServerPort", packet.ReadString());
        Assert.Equal("3000", packet.ReadString());
        Assert.Equal("socketPolicyPort", packet.ReadString());
        Assert.Equal("host", packet.ReadString());
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public void CapturedGameListAndLoadPacketsPreserveExactFieldsAndRecompose()
    {
        var source = new List<GameData> { Game() };
        var list = new GameListComposer(source.Select(GameListEntry.Capture).ToImmutableArray());
        var load = new LoadGameComposer(GameLoadSnapshot.Capture(source[0]), "ticket");
        source.Clear();

        for (var index = 0; index < 2; index++) {
            var listPacket = new HabbiconTestSupport.RecordingPacket();
            list.Compose(listPacket);
            Assert.Equal(new object[] { 1, 9, "game", "AA", "BB", "path/", "three" }, listPacket.Writes);
            var loadPacket = new HabbiconTestSupport.RecordingPacket();
            load.Compose(loadPacket);
            Assert.Equal(new object[]
            {
                9, "1365260055982", "path/game.swf", "best", "showAll", 60, 10, 8, 6,
                "assetUrl", "path/assets", "habboHost", "http://fuseus-private-httpd-fe-1", "accessToken", "ticket",
                "gameServerHost", "host", "gameServerPort", "3000", "socketPolicyPort", "host"
            }, loadPacket.Writes);
        }
    }

    [Fact]
    public void ServiceShowsTheCurrentGameListIncludingEmptyLists()
    {
        var games = new Games();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        var service = new GameLobbyService(games);
        service.ShowGames(client);
        Assert.Equal(0, new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload }.ReadInt());
        games.GameData.Add(Game());
        sent.Clear();
        service.ShowGames(client);
        Assert.Equal(ServerPacketHeader.GameListComposer, Assert.Single(sent).Header);
        Assert.Equal(1, new FlashIncomingPacket { Buffer = sent[0].Payload }.ReadInt());
    }

    private static GameData Game() => new(9, "game", "AA", "BB", "path/", "three", "game.swf", "assets", "host", "3000", true);

    private sealed class Games : IGameDataManager
    {
        public ICollection<GameData> GameData { get; } = new List<GameData>();
        public void Init() => throw new NotSupportedException();
        public bool TryGetGame(int gameId, [NotNullWhen(true)] out GameData? data)
        {
            data = GameData.FirstOrDefault(game => game.Id == gameId);

            return data != null;
        }
        public int GetCount() => GameData.Count;
    }

    private sealed class RecordingService : IGameLobbyService
    {
        public bool ListRequested { get; private set; }
        public int GameId { get; private set; }
        public void ShowGames(GameClient session) => ListRequested = true;
        public void JoinQueue(GameClient session, int gameId) => GameId = gameId;
    }
}
