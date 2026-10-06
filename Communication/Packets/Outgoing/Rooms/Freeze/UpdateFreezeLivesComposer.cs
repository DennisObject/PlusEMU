using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Freeze;

public class UpdateFreezeLivesComposer : IServerPacket
{
    private readonly int _userId;
    private readonly int _freezeLives;
    public uint MessageId => ServerPacketHeader.UpdateFreezeLivesComposer;

    public UpdateFreezeLivesComposer(int userId, int freezeLives)
    {
        _userId = userId;
        _freezeLives = freezeLives;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_userId);
        packet.WriteInteger(_freezeLives);
    }
}
