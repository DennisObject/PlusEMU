using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredClickUserResponseComposer(int virtualId, bool openMenu, uint roomId = 0, int requestId = 0, bool doNotRotate = false) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredClickUserResponseComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(virtualId);
        packet.WriteBoolean(openMenu);

        if (requestId != 0) {
            packet.WriteInteger(1);
            packet.WriteInteger(unchecked((int)roomId));
            packet.WriteInteger(requestId);
            packet.WriteBoolean(doNotRotate);
        }
    }
}
