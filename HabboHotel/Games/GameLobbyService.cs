using System.Collections.Immutable;
using System.Text;
using Plus.Communication.Packets.Outgoing.Game;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Games;

public sealed record GameListEntry(int Id, string Name, string ColourOne, string ColourTwo, string ResourcePath, string StringThree)
{
    public static GameListEntry Capture(GameData game) =>
        new(game.Id, game.Name, game.ColourOne, game.ColourTwo, game.ResourcePath, game.StringThree);
}

public sealed record GameLoadSnapshot(int Id, string SwfUrl, string AssetsUrl, string ServerHost, string ServerPort)
{
    public static GameLoadSnapshot Capture(GameData game) =>
        new(game.Id, game.ResourcePath + game.Swf, game.ResourcePath + game.Assets, game.ServerHost, game.ServerPort);
}

public interface IGameLobbyService
{
    void ShowGames(GameClient session);
    void JoinQueue(GameClient session, int gameId);
}

public sealed class GameLobbyService(IGameDataManager games) : IGameLobbyService
{
    public void ShowGames(GameClient session) =>
        session.Send(new GameListComposer(games.GameData.Select(GameListEntry.Capture).ToImmutableArray()));

    public void JoinQueue(GameClient session, int gameId)
    {
        if (!games.TryGetGame(gameId, out var game))
            return;
        var snapshot = GameLoadSnapshot.Capture(game);
        var ticket = $"HABBOON-Fastfood-{GenerateSso(32)}-{session.GetHabbo().Id}";
        session.Send(new JoinQueueComposer(snapshot.Id));
        session.Send(new LoadGameComposer(snapshot, ticket));
    }

    private static string GenerateSso(int length)
    {
        const string characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        var result = new StringBuilder(length);
        for (var index = 0; index < length; index++) result.Append(characters[Random.Shared.Next(characters.Length)]);
        return result.ToString();
    }
}
