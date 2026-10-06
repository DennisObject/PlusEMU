using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Avatar;

public class AvatarAspectUpdateComposer : IServerPacket
{
    private readonly string _figure;
    private readonly string _gender;

    public uint MessageId => ServerPacketHeader.AvatarAspectUpdateComposer;

    public AvatarAspectUpdateComposer(string figure, string gender)
    {
        _figure = figure;
        _gender = gender;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_figure);
        packet.WriteString(_gender);
    }
}
