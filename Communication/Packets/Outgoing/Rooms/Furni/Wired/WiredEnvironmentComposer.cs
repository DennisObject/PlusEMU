using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredEnvironmentComposer(bool hasClickUser, uint roomId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredEnvironmentComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(hasClickUser);
        packet.WriteInteger(0); // No achievement actions are enabled by this room implementation.
        packet.WriteInteger(1);
        packet.WriteInteger(unchecked((int)roomId));
    }
}
