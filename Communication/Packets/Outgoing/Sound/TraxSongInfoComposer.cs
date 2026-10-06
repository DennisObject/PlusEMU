using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Sound;

// TODO @80O: Implement
public class TraxSongInfoComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.TraxSongInfoComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(0); //Count;
}
