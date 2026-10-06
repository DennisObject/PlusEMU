using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Outgoing.Rooms.AI.Pets;

public sealed class PetHorseFigureInformationComposer(HorseAppearanceSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PetHorseFigureInformationComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(data.VirtualId);
        packet.WriteInteger(data.PetId);
        packet.WriteInteger(data.Type);
        packet.WriteInteger(data.Race);
        packet.WriteString(data.Color);
        if (data.Saddle > 0)
        {
            packet.WriteInteger(4); packet.WriteInteger(3); packet.WriteInteger(3);
            packet.WriteInteger(data.Hair); packet.WriteInteger(data.HairDye);
            packet.WriteInteger(2); packet.WriteInteger(data.Hair); packet.WriteInteger(data.HairDye);
            packet.WriteInteger(4); packet.WriteInteger(data.Saddle); packet.WriteInteger(0);
        }
        else
        {
            packet.WriteInteger(1); packet.WriteInteger(2); packet.WriteInteger(2);
            packet.WriteInteger(data.Hair); packet.WriteInteger(data.HairDye);
            packet.WriteInteger(3); packet.WriteInteger(data.Hair); packet.WriteInteger(data.HairDye);
        }
        packet.WriteBoolean(data.Saddle > 0);
        packet.WriteBoolean(data.Riding);
    }
}
