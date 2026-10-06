using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CheckPetNameComposer : IServerPacket
{
    private readonly PetNameError _error;
    private readonly string _extraData;
    public uint MessageId => ServerPacketHeader.CheckPetNameComposer;

    public CheckPetNameComposer(PetNameError error, string extraData)
    {
        _error = error;
        _extraData = extraData;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)_error); //0 = nothing, 1 = too long, 2 = too short, 3 = invalid characters
        packet.WriteString(_extraData);
    }
}
