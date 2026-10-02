using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

public sealed class WiredValidationErrorComposer(string error) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredValidationErrorComposer;
    public void Compose(IOutgoingPacket packet) => packet.WriteString(error);
}
