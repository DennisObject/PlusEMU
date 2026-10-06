namespace Plus.HabboHotel.Games;

public class GameData
{
    public GameData(int gameId, string name, string colourOne, string colourTwo, string resourcePath, string stringThree, string gameSwf, string gameAssets, string gameServerHost,
        string gameServerPort, string socketPolicyPort, bool enabled)
    {
        Id = gameId;
        Name = name;
        ColourOne = colourOne;
        ColourTwo = colourTwo;
        ResourcePath = resourcePath;
        StringThree = stringThree;
        Swf = gameSwf;
        Assets = gameAssets;
        ServerHost = gameServerHost;
        ServerPort = gameServerPort;
        SocketPolicyPort = socketPolicyPort;
        Enabled = enabled;
    }

    public int Id { get; }
    public string Name { get; }
    public string ColourOne { get; }
    public string ColourTwo { get; }
    public string ResourcePath { get; }
    public string StringThree { get; }
    public string Swf { get; }
    public string Assets { get; }
    public string ServerHost { get; }
    public string ServerPort { get; }
    public string SocketPolicyPort { get; }
    public bool Enabled { get; }
}
