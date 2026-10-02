using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredClickUserResponseComposer(int virtualId, bool openMenu) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredClickUserResponseComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(virtualId);
        packet.WriteBoolean(openMenu);
    }
}
