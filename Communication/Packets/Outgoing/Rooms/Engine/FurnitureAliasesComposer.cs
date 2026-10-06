using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

// TODO @80O: Implement
public class FurnitureAliasesComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.FurnitureAliasesComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(0);
}
