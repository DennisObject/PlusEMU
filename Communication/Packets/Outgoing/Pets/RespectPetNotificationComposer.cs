using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Pets;

public class RespectPetNotificationComposer : IServerPacket
{
    private readonly int _virtualId;
    private readonly int _petId;
    private readonly string _name;
    private readonly string _colour;

    public uint MessageId => ServerPacketHeader.RespectPetNotificationComposer;

    public RespectPetNotificationComposer(int virtualId, int petId, string name, string colour)
    {
        _virtualId = virtualId;
        _petId = petId;
        _name = name;
        _colour = colour;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_virtualId);
        packet.WriteInteger(_virtualId);
        packet.WriteInteger(_petId);
        packet.WriteString(_name);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteString(_colour);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(1);
    }
}