using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;

public class AddExperiencePointsComposer : IServerPacket
{
    private readonly int _petId;
    private readonly int _virtualId;
    private readonly int _amount;
    public uint MessageId => ServerPacketHeader.AddExperiencePointsComposer;

    public AddExperiencePointsComposer(int petId, int virtualId, int amount)
    {
        _petId = petId;
        _virtualId = virtualId;
        _amount = amount;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_petId);
        packet.WriteInteger(_virtualId);
        packet.WriteInteger(_amount);
    }
}
