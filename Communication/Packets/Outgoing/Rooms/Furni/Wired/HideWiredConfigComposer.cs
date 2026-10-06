using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public class HideWiredConfigComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HideWiredConfigComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
