using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;

namespace Plus.Communication.Packets.Outgoing.Game;

// TODO @80O: Implement
public class LoadGameComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.LoadGameComposer;

    private readonly GameLoadSnapshot _gameData;
    private readonly string _ssoTicket;

    public LoadGameComposer(GameLoadSnapshot gameData, string ssoTicket)
    {
        _gameData = gameData;
        _ssoTicket = ssoTicket;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_gameData.Id);
        packet.WriteString("1365260055982");
        packet.WriteString(_gameData.SwfUrl);
        packet.WriteString("best");
        packet.WriteString("showAll");
        packet.WriteInteger(60); //FPS?
        packet.WriteInteger(10);
        packet.WriteInteger(8);
        packet.WriteInteger(6); //Asset count
        packet.WriteString("assetUrl");
        packet.WriteString(_gameData.AssetsUrl);
        packet.WriteString("habboHost");
        packet.WriteString("http://fuseus-private-httpd-fe-1");
        packet.WriteString("accessToken");
        packet.WriteString(_ssoTicket);
        packet.WriteString("gameServerHost");
        packet.WriteString(_gameData.ServerHost);
        packet.WriteString("gameServerPort");
        packet.WriteString(_gameData.ServerPort);
        packet.WriteString("socketPolicyPort");
        packet.WriteString(_gameData.ServerHost);

    }
}