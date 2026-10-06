using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public sealed class ManageGroupComposer(GroupManagementSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ManageGroupComposer;

    public void Compose(IOutgoingPacket packet)
    {
        if (data.HasRoom) {
            packet.WriteInteger(1);
            packet.WriteInteger((int)data.RoomId);
            packet.WriteString(data.RoomName);
            packet.WriteBoolean(false);
        }
        else {
            packet.WriteInteger(0);
        }

        packet.WriteBoolean(true);
        packet.WriteInteger(data.GroupId);
        packet.WriteString(data.Name);
        packet.WriteString(data.Description);
        packet.WriteInteger((int)data.RoomId);
        packet.WriteInteger(data.Colour1);
        packet.WriteInteger(data.Colour2);
        packet.WriteInteger(data.Type);
        packet.WriteInteger(data.AdminOnlyDeco);
        packet.WriteBoolean(false);
        packet.WriteString(string.Empty);
        packet.WriteInteger(5);

        foreach (var piece in data.BadgePieces) {
            packet.WriteInteger(piece.Symbol);
            packet.WriteInteger(piece.Colour);
            packet.WriteInteger(piece.Position);
        }

        packet.WriteString(data.Badge);
        packet.WriteInteger(data.MemberCount);
        packet.WriteBoolean(data.ForumEnabled);
    }
}
